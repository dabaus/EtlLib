﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using EtlLib.Data;
using EtlLib.Logging;
using EtlLib.Nodes;
using EtlLib.Pipeline.Builders;

namespace EtlLib.Pipeline
{
    public class EtlProcess
    {
        private readonly EtlProcessContext _processContext;
        private readonly ILogger _log;
        private readonly List<IInputOutputAdapter> _ioAdapters;
        private readonly List<string> _attachmentDeduplicationList;
        private readonly List<INode> _nodes;
        private readonly EtlProcessSettings _settings;

        public INodeWithOutput RootNode { get; }
        public string Name { get; }

        public EtlProcess(EtlProcessSettings settings, EtlProcessContext context)
        {
            _settings = settings;

            Name = settings.Name;

            _ioAdapters = new List<IInputOutputAdapter>();
            _attachmentDeduplicationList = new List<string>();
            _nodes = new List<INode>();
            
            _processContext = context;

            _log = settings.LoggingAdapter.CreateLogger("EtlLib.EtlProcess");
        }

        private void RegisterNode(INode node)
        {
            if (_nodes.Contains(node))
                return;

            node.SetContext(_processContext);
            _nodes.Add(node);
        }

        private void RegisterNodes(params INode[] nodes)
        {
            foreach(var node in nodes)
                RegisterNode(node);
        }

        public void AttachInputToOutput<T>(INodeWithOutput<T> output, INodeWithInput<T> input) where T : class, IFreezable
        {
            var dedupHash = $"{output.Id}:{input.Id}";

            if (_attachmentDeduplicationList.Contains(dedupHash))
            {
                _log.Debug($"Node {input} is already attached to output {output}");
                return;
            }

            RegisterNodes(input, output);

            var ioAdapter = _ioAdapters.SingleOrDefault(x => x.OutputNode.Equals(output)) as InputOutputAdapter<T>;
            if (ioAdapter == null)
            {
                ioAdapter = new InputOutputAdapter<T>(output)
                    .WithLogger(_settings.LoggingAdapter.CreateLogger("EtlLib.IOAdapter"));

                output.SetEmitter(ioAdapter);

                _ioAdapters.Add(ioAdapter);
            }

            if (input is INodeWithInput2<T> input2)
            {
                ioAdapter.AttachConsumer(input2);

                if (input2.Input == null)
                {
                    _log.Info($"Attaching {input} input port #1 to output port of {output}.");
                    input.SetInput(ioAdapter.GetConsumingEnumerable(input));
                }
                else if (input2.Input2 == null)
                {
                    _log.Info($"Attaching {input} input port #2 to output port of {output}.");
                    input2.SetInput2(ioAdapter.GetConsumingEnumerable(input2));
                }
            }
            else
            {
                ioAdapter.AttachConsumer(input);

                _log.Info($"Attaching {input} input port #1 to output port of {output}.");
                input.SetInput(ioAdapter.GetConsumingEnumerable(input));
            }
            
            _attachmentDeduplicationList.Add($"{output.Id}:{input.Id}");
        }

        public void Execute()
        {
            _log.Info($"=== Executing ETL Process '{Name}' (Started {DateTime.Now}) ===");

            if (_settings.ContextInitializer != null)
            {
                _log.Info("Running context initializer.");
                _settings.ContextInitializer(_processContext);
            }

            var tasks = new List<Task>();
            var processStopwatch = Stopwatch.StartNew();

            foreach (var node in _nodes)
            {
                var task = Task.Run(() =>
                    {
                        _log.Info($"Beginning execute task for node {node}.");
                        var sw = Stopwatch.StartNew();
                        node.Execute();
                        sw.Stop();
                        _log.Info($"Execute task for node {node} has completed in {sw.Elapsed}.");
                    });

                tasks.Add(task);
            }

            Task.WaitAll(tasks.ToArray());

            processStopwatch.Stop();

            _log.Info($"=== ETL Process '{Name}' has completed (Runtime {processStopwatch.Elapsed}) ===");

            _log.Debug("Disposing of all input/output adapters.");
            _ioAdapters.ForEach(x => x.Dispose());

            _log.Debug("Performing garbage collection of all generations.");
            GC.Collect();
        }
    }
}