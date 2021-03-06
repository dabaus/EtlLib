﻿using System.Collections.Generic;
using System.IO;
using System.Text;
using CsvHelper;
using EtlLib.Data;
using EtlLib.Pipeline;

namespace EtlLib.Nodes.CsvFiles
{
    public class CsvWriterNode : AbstractInputOutputNode<Row, NodeOutputWithFilePath>
    {
        private string _filePath;
        private bool _includeHeader;
        private int _writtenRowCount;
        private Encoding _encoding;

        public CsvWriterNode(string filePath)
        {
            _filePath = filePath;
            _includeHeader = true;

            _encoding = Encoding.UTF8;
        }

        public CsvWriterNode IncludeHeader(bool includeHeaders = true)
        {
            _includeHeader = includeHeaders;
            return this;
        }

        public CsvWriterNode WithEncoding(Encoding encoding)
        {
            _encoding = encoding;
            return this;
        }

        public override void OnExecute(EtlPipelineContext context)
        {
            var log = context.GetLogger("EtlLib.Nodes.CsvWriterNode");
            var first = true;
            var columns = new List<string>();

            using (var file = File.OpenWrite(_filePath))
            using (var sw = new StreamWriter(file, _encoding))
            using (var writer = new CsvWriter(sw))
            {
                foreach (var row in Input)
                {
                    if (first && _includeHeader)
                    {
                        foreach (var column in row)
                        {
                            writer.WriteField(column.Key);
                            columns.Add(column.Key);
                        }
                        writer.NextRecord();
                        first = false;
                    }

                    foreach (var column in row)
                    {
                        writer.WriteField(column.Value);
                    }
                    writer.NextRecord();

                    context.ObjectPool.Return(row);
                    _writtenRowCount++;
                }

                writer.Flush();
            }

            if (_includeHeader)
                _writtenRowCount++;

            log.Debug($"{this} wrote {_writtenRowCount} rows to '{_filePath}");

            Emit(new CsvWriterNodeResult(_filePath, _writtenRowCount, columns.ToArray(), _includeHeader));
            SignalEnd();
        }
    }
}