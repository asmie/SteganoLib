using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace SteganoLib.Crypto
{
    public sealed class PRNG
    {

        public void Initialize(int seed)
        {
            _random = CreateInstance(Name, seed)
                ?? throw new InvalidOperationException($"PRNG algorithm '{Name}' is not registered.");
        }

        /// <summary>
        /// Return next random.
        /// </summary>
        /// <returns>Random integer.</returns>
        public int Next()
        {
            if (_random == null)
                throw new InvalidOperationException("PRNG has not been initialized. Call Initialize() first.");
            return _random.Next();
        }

        /// <summary>
        /// Return next random not greater than max.
        /// </summary>
        /// <param name="max">max value</param>
        /// <returns>Random integer.</returns>
        public int Next(int max)
        {
            if (_random == null)
                throw new InvalidOperationException("PRNG has not been initialized. Call Initialize() first.");
            return _random.Next(max);
        }


        /// <summary>
        /// Return next random between min and max.
        /// </summary>
        /// <param name="min">min random value</param>
        /// <param name="max">max random value</param>
        /// <returns>Random integer</returns>
        public int Next(int min, int max)
        {
            if (_random == null)
                throw new InvalidOperationException("PRNG has not been initialized. Call Initialize() first.");
            return _random.Next(min, max);
        }


        /// <summary>
        /// Name of the algorithm to use during operations.
        /// </summary>
        public string Name
        {
            get; set;
        } = "Random";

        /// <summary>
        /// Internal object representation.
        /// Can be built-in Random type as every registered PRNG must inherit from Random class.
        /// </summary>
        private Random _random;

        /// <summary>
        /// Static method that creates instance of PRNG that is chosen by name.
        /// </summary>
        /// <param name="name">PRNG name.</param>
        /// <param name="seed">Seed passed to the PRNG constructor.</param>
        /// <returns>Created instance of PRNG or null if name was not found in the registered PRNG.</returns>
        private static Random CreateInstance(string name, int seed)
        {
            if (_registeredPRNG.TryGetValue(name, out var type))
                return (Random)Activator.CreateInstance(type, seed);

            return null;
        }

        /// <summary>
        /// Register a custom PRNG implementation. The type must derive from <see cref="Random"/>.
        /// Names are unique: re-registering an existing name returns <c>false</c> and leaves
        /// the existing registration untouched (no silent shadowing).
        /// </summary>
        /// <param name="name">Name of the PRNG.</param>
        /// <param name="creator">Type to be used for PRNG creation.</param>
        /// <returns><c>true</c> if registered; <c>false</c> if the type is not a <see cref="Random"/> subclass, or if a registration with the same name already exists.</returns>
        public static bool RegisterPRNG(string name, Type creator)
        {
            if (!creator.IsSubclassOf(typeof(Random)) && creator != typeof(Random))
                return false;

            return _registeredPRNG.TryAdd(name, creator);
        }

        /// <summary>
        /// Currently registered PRNG implementations, keyed by name.
        /// </summary>
        private static readonly ConcurrentDictionary<string, Type> _registeredPRNG = new(
            new[]
            {
                new KeyValuePair<string, Type>("Random", typeof(Random)),
            });
    }
}
