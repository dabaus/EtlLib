﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using EtlLib.Data;
using EtlLib.Logging;
using EtlLib.Nodes;

namespace EtlLib.Pipeline
{
    public interface IInputOutputAdapter
    {
        INode OutputNode { get; }

        bool AttachConsumer<T>(INodeWithInput<T> input) 
            where T : class, IFreezable;

        bool AttachConsumer<T>(INodeWithInput2<T> input)
            where T : class, IFreezable;
    }

    public class InputOutputAdapter<T> : IInputOutputAdapter, IEmitter<T>
        where T : class, IFreezable
    {
        private readonly ConcurrentDictionary<Guid, BlockingCollection<T>> _queueMap;
        private readonly ConcurrentBag<INodeWithInput<T>> _inputs;
        private readonly INodeWithOutput<T> _output;
        private ILogger _log;
        private long _emittedItems;

        public INode OutputNode => _output;

        public InputOutputAdapter(INodeWithOutput<T> output)
        {
            _queueMap = new ConcurrentDictionary<Guid, BlockingCollection<T>>();
            _inputs = new ConcurrentBag<INodeWithInput<T>>();
            _output = output;
            _log = new NullLogger();
        }

        public InputOutputAdapter<T> WithLogger(ILogger log)
        {
            _log = log;
            return this;
        }

        public IEnumerable<T> GetConsumingEnumerable(Guid nodeId)
        {
            return _queueMap[nodeId].GetConsumingEnumerable();
        }

        public IEnumerable<T> GetConsumingEnumerable(INodeWithInput<T> node)
        {
            return _queueMap[node.Id].GetConsumingEnumerable();
        }

        public bool AttachConsumer<TIn>(INodeWithInput<TIn> input)
            where TIn : class, IFreezable
        {
            if (input.Input != null)
                throw new InvalidOperationException($"Node (Id={input.Id}, Type={input.GetType().Name}) already has an input assigned.");

            if (!_queueMap.TryAdd(input.Id, new BlockingCollection<T>(new ConcurrentQueue<T>())))
                return false;

            _inputs.Add((INodeWithInput<T>)input);

            return true;
        }

        public bool AttachConsumer<TIn>(INodeWithInput2<TIn> input)
            where TIn : class, IFreezable
        {
            if (input.Input != null && input.Input2 != null)
                throw new InvalidOperationException($"Node (Id={input.Id}, Type={input.GetType().Name}) has two input slots of which both are already assigned.");

            if (!_queueMap.TryAdd(input.Id, new BlockingCollection<T>(new ConcurrentQueue<T>())))
                return false;

            _inputs.Add((INodeWithInput<T>)input);
            return true;
        }

        public void Emit(T item)
        {
            if (_emittedItems == 0)
                _log.Debug($"Node {_output} emitting its first item.");

            item.Freeze();
            _emittedItems++;

            foreach (var queue in _queueMap.Values)
                queue.Add(item);

            if (_emittedItems % 5000 == 0)
                _log.Debug($"Node {_output} has emitted {_emittedItems} items.");
        }

        public void SignalEnd()
        {
            _log.Debug($"Node {_output} has signalled the end of its data stream (emitted {_emittedItems} total items).");

            foreach (var queue in _queueMap.Values)
                queue.CompleteAdding();
        }
    }
}