using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace DotChess.EventSourcing
{
    /// <summary>
    /// Event Store em memória com ordem garantida por partida e controle de concorrência.
    /// </summary>
    public class InMemoryEventStore
    {
        private readonly ConcurrentDictionary<string, EventStream> _streams =
            new ConcurrentDictionary<string, EventStream>();

        private class EventStream
        {
            public List<DomainEvent> Events { get; } = new List<DomainEvent>();
            public object LockObj { get; } = new object();
            public int Version { get; set; } = 0;
        }

        /// <summary>
        /// Persiste eventos de uma partida. Garante ordem e incrementa versão atomicamente.
        /// </summary>
        public void AppendEvents(string matchId, IEnumerable<DomainEvent> events)
        {
            if (string.IsNullOrEmpty(matchId))
                throw new ArgumentException("matchId required");

            var stream = _streams.GetOrAdd(matchId, _ => new EventStream());

            lock (stream.LockObj)
            {
                int expectedVersion = stream.Version + 1;

                foreach (var evt in events)
                {
                    if (evt.EventVersion != expectedVersion)
                        throw new ConcurrencyException(
                            $"Version conflict: expected {expectedVersion}, got {evt.EventVersion}");

                    evt.EventVersion = expectedVersion;
                    stream.Events.Add(evt);
                    stream.Version = expectedVersion;
                    expectedVersion++;
                }
            }
        }

        /// <summary>
        /// Recupera todos os eventos de uma partida em ordem.
        /// </summary>
        public IEnumerable<DomainEvent> GetEvents(string matchId)
        {
            if (!_streams.TryGetValue(matchId, out var stream))
                return new List<DomainEvent>();

            lock (stream.LockObj)
            {
                return stream.Events.ToList();
            }
        }

        /// <summary>
        /// Reconstrói o estado atual de uma partida a partir dos eventos.
        /// </summary>
        public ChessMatch Replay(string matchId)
        {
            var events = GetEvents(matchId);
            if (!events.Any())
                throw new InvalidOperationException($"No events found for match {matchId}");

            return ChessMatch.LoadFromHistory(events);
        }

        /// <summary>
        /// Retorna versão atual de uma partida (número total de eventos).
        /// </summary>
        public int GetCurrentVersion(string matchId)
        {
            if (_streams.TryGetValue(matchId, out var stream))
            {
                lock (stream.LockObj)
                {
                    return stream.Version;
                }
            }
            return 0;
        }

        /// <summary>
        /// Lista todas as partidas armazenadas.
        /// </summary>
        public IEnumerable<string> GetAllMatchIds()
        {
            return _streams.Keys.ToList();
        }
    }

    public class ConcurrencyException : Exception
    {
        public ConcurrencyException(string message) : base(message) { }
    }
}
