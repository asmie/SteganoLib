#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace SteganoLib.Crypto
{
    /// <summary>
    /// Named, seeded random generator. Registrations are process-wide and thread-safe;
    /// each instance requires external synchronisation when shared between threads.
    /// </summary>
    public sealed class PRNG
    {

        /// <summary>Creates a generator using the current name. Failure preserves the previous generator and its position.</summary>
        public void Initialize(int seed)
        {
            string name = Name;
            if (!_registeredPRNG.TryGetValue(name, out var factory))
                throw new InvalidOperationException($"PRNG algorithm '{name}' is not registered.");
            _random = factory(seed)
                ?? throw new InvalidOperationException($"PRNG factory '{name}' returned null.");
        }

        /// <summary>
        /// Returns a nonnegative random integer less than <see cref="int.MaxValue"/>.
        /// </summary>
        /// <returns>Random integer.</returns>
        public int Next()
        {
            if (_random == null)
                throw new InvalidOperationException("PRNG has not been initialized. Call Initialize() first.");
            return _random.Next();
        }

        /// <summary>
        /// Returns a nonnegative random integer less than <paramref name="max"/>, or zero when the bound is zero.
        /// </summary>
        /// <param name="max">Exclusive upper bound; must be nonnegative.</param>
        /// <returns>Random integer.</returns>
        public int Next(int max)
        {
            if (_random == null)
                throw new InvalidOperationException("PRNG has not been initialized. Call Initialize() first.");
            return _random.Next(max);
        }


        /// <summary>
        /// Returns an integer in [min, max), or <paramref name="min"/> when the bounds are equal.
        /// </summary>
        /// <param name="min">Inclusive lower bound.</param>
        /// <param name="max">Exclusive upper bound; must be at least <paramref name="min"/>.</param>
        /// <returns>Random integer</returns>
        public int Next(int min, int max)
        {
            if (_random == null)
                throw new InvalidOperationException("PRNG has not been initialized. Call Initialize() first.");
            return _random.Next(min, max);
        }


        /// <summary>
        /// Name used by the next successful <see cref="Initialize"/>. Changing it does not reset an existing generator.
        /// </summary>
        public string Name
        {
            get => _name;
            set
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(value);
                _name = value;
            }
        }

        private string _name = "Random";

        /// <summary>
        /// Internal object representation.
        /// Can be built-in Random type as every registered PRNG must inherit from Random class.
        /// </summary>
        private Random? _random;

        /// <summary>
        /// Register a concrete, closed <see cref="Random"/> type with a public constructor taking one <see cref="int"/>.
        /// Names are unique: re-registering an existing name returns <c>false</c> and leaves
        /// the existing registration untouched (no silent shadowing).
        /// </summary>
        /// <param name="name">Name of the PRNG.</param>
        /// <param name="creator">Type to be used for PRNG creation.</param>
        /// <returns><c>true</c> if registered; <c>false</c> for an unsupported type or an existing name.</returns>
        public static bool RegisterPRNG(string name, Type creator)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(creator);
            if (!typeof(Random).IsAssignableFrom(creator) || creator.IsAbstract || creator.ContainsGenericParameters)
                return false;

            var constructor = creator.GetConstructor(BindingFlags.Public | BindingFlags.Instance | BindingFlags.ExactBinding,
                binder: null, types: new[] { typeof(int) }, modifiers: null);
            if (constructor == null)
                return false;
            return RegisterPRNGFactory(name, seed => (Random)constructor.Invoke(new object[] { seed }));
        }

        /// <summary>
        /// Registers a factory without calling it. Each call must return a fresh generator seeded
        /// with the supplied value. Factories may be called concurrently; exceptions propagate
        /// from <see cref="Initialize"/> and null results cause <see cref="InvalidOperationException"/>.
        /// Names are case-sensitive; an existing name returns false and is left unchanged.
        /// </summary>
        public static bool RegisterPRNGFactory(string name, Func<int, Random> factory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(factory);
            return _registeredPRNG.TryAdd(name, factory);
        }

        private static readonly ConcurrentDictionary<string, Func<int, Random>> _registeredPRNG = new(
            new[]
            {
                new KeyValuePair<string, Func<int, Random>>("Random", seed => new Random(seed)),
            });
    }
}
