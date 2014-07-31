﻿using System;
using System.Collections.Generic;
using System.Linq;
using d60.EventSorcerer.Extensions;

namespace d60.EventSorcerer.Events
{
    public class EventValidation
    {
        public static void ValidateBatchIntegrity(Guid batchId, List<DomainEvent> events)
        {
            EnsureAllEventsHaveSequenceNumbers(events);

            EnsureAllEventsHaveAggregateRootId(events);

            EnsureSeq(batchId, events);
        }

        static void EnsureAllEventsHaveAggregateRootId(List<DomainEvent> events)
        {
            if (events.Any(e => !e.Meta.ContainsKey(DomainEvent.MetadataKeys.AggregateRootId)))
            {
                throw new InvalidOperationException("Can't save batch with event without an aggregate root id");
            }
        }

        static void EnsureAllEventsHaveSequenceNumbers(List<DomainEvent> events)
        {
            if (events.Any(e => !e.Meta.ContainsKey(DomainEvent.MetadataKeys.SequenceNumber)))
            {
                throw new InvalidOperationException("Can't save batch with event without a sequence number");
            }
        }

        static void EnsureSeq(Guid batchId, List<DomainEvent> events)
        {
            var seqs = events
                .GroupBy(e => e.GetAggregateRootId())
                .ToDictionary(g => g.Key, g => g.Min(e => e.GetSequenceNumber()));

            foreach (var e in events)
            {
                var sequenceNumberOfThisEvent = e.GetSequenceNumber();
                var aggregateRootId = e.GetAggregateRootId();
                var expectedSequenceNumber = seqs[aggregateRootId];

                if (sequenceNumberOfThisEvent != expectedSequenceNumber)
                {
                    throw new InvalidOperationException(
                        string.Format(@"Attempted to save batch {0} which contained events with non-sequential sequence numbers!

{1}", batchId,
                            string.Join(Environment.NewLine,
                                events.Select(
                                    ev => string.Format("    {0} / {1}", ev.GetAggregateRootId(), ev.GetSequenceNumber())))));
                }

                seqs[aggregateRootId]++;
            }
        }
    }
}