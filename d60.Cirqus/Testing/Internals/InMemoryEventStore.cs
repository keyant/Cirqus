using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using d60.Cirqus.Events;
using d60.Cirqus.Exceptions;
using d60.Cirqus.Extensions;
using d60.Cirqus.Numbers;
using d60.Cirqus.Serialization;

namespace d60.Cirqus.Testing.Internals
{
    class InMemoryEventStore : IEventStore, IEnumerable<DomainEvent>
    {
        readonly IDomainEventSerializer _domainEventSerializer;
        readonly Dictionary<string, object> _idAndSeqNoTuples = new Dictionary<string, object>();
        readonly List<EventBatch> _savedEventBatches = new List<EventBatch>();
        readonly object _lock = new object();

        long _globalSequenceNumber;

        public InMemoryEventStore(IDomainEventSerializer domainEventSerializer)
        {
            _domainEventSerializer = domainEventSerializer;
        }

        public void Save(Guid batchId, IEnumerable<EventData> events)
        {
            var batch = events.ToList();

            var tuplesInBatch = batch
                .Select(e => string.Format("{0}:{1}", e.GetAggregateRootId(), e.GetSequenceNumber()))
                .ToList();

            lock (_lock)
            {
                try
                {
                    foreach (var tuple in tuplesInBatch)
                    {
                        if (_idAndSeqNoTuples.ContainsKey(tuple))
                        {
                            throw new InvalidOperationException(string.Format("Found duplicate: {0}", tuple));
                        }
                    }
                }
                catch (Exception exception)
                {
                    throw new ConcurrencyException(batchId, batch, exception);
                }

                foreach (var tuple in tuplesInBatch)
                {
                    _idAndSeqNoTuples.Add(tuple, null);
                }

                var sequenceNumbersToAllocate = batch.Count;

                var result = Interlocked.Add(ref _globalSequenceNumber, sequenceNumbersToAllocate);

                result -= sequenceNumbersToAllocate;

                foreach (var e in batch)
                {
                    e.Meta[DomainEvent.MetadataKeys.GlobalSequenceNumber] = (result++).ToString(Metadata.NumberCulture);
                    e.Meta[DomainEvent.MetadataKeys.BatchId] = batchId.ToString();
                }

                EventValidation.ValidateBatchIntegrity(batchId, batch);

                _savedEventBatches.Add(new EventBatch(batchId, batch.Select(Clone)));
            }
        }

        public IEnumerable<EventData> Load(string aggregateRootId, long firstSeq = 0)
        {
            lock (_lock)
            {
                return _savedEventBatches
                    .SelectMany(b => b.Events)
                    .Select(e => new
                    {
                        Event = e,
                        AggregateRootId = e.GetAggregateRootId(),
                        SequenceNumber = e.GetSequenceNumber()
                    })
                    .Where(e => e.AggregateRootId == aggregateRootId)
                    .Where(e => e.SequenceNumber >= firstSeq)
                    .OrderBy(e => e.SequenceNumber)
                    .Select(e => Clone(e.Event))
                    .ToList();
            }
        }

        public IEnumerable<EventData> Stream(long globalSequenceNumber = 0)
        {
            lock (_lock)
            {
                return _savedEventBatches
                    .SelectMany(e => e.Events)
                    .Select(@event => new
                    {
                        Event = @event,
                        GlobalSequenceNumner = @event.GetGlobalSequenceNumber()
                    })
                    .Where(a => a.GlobalSequenceNumner >= globalSequenceNumber)
                    .OrderBy(a => a.GlobalSequenceNumner)
                    .Select(a => Clone(a.Event))
                    .ToList();
            }
        }

        public long GetNextGlobalSequenceNumber()
        {
            return _globalSequenceNumber;
        }

        public IEnumerator<DomainEvent> GetEnumerator()
        {
            lock (_lock)
            {
                var clone = _savedEventBatches
                    .SelectMany(b => b.Events)
                    .Select(e => _domainEventSerializer.Deserialize(e))
                    .ToList();

                return clone.GetEnumerator();
            }
        }

        public long GetNextSeqNo(string aggregateRootId)
        {
            lock (_lock)
            {
                var domainEvents = _savedEventBatches
                    .SelectMany(b => b.Events)
                    .Where(e => e.GetAggregateRootId() == aggregateRootId)
                    .ToList();

                return domainEvents.Any()
                    ? domainEvents.Max(e => e.GetSequenceNumber()) + 1
                    : 0;
            }
        }

        EventData Clone(EventData arg)
        {
            var meta = arg.Meta;
            var data = arg.Data;

            var newMeta = meta.Clone();
            var newData = new byte[data.Length];
            
            Array.Copy(data, newData, data.Length);
            
            return EventData.FromMetadata(newMeta, newData);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        class EventBatch
        {
            public EventBatch(Guid batchId, IEnumerable<EventData> events)
            {
                BatchId = batchId;
                Events = events.ToList();
            }

            public Guid BatchId { get; private set; }
            public List<EventData> Events { get; private set; }
        }
    }
}