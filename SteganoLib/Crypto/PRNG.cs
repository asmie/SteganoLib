using System;
using System.Collections.Generic;
using System.Text;

namespace SteganoLib.Crypto
{
    public sealed class PRNG
    {

        public void Initialize(int seed)
        {
            _random = CreateInstance(Name, seed);
        }

        /// <summary>
        /// Return next random.
        /// </summary>
        /// <returns>Random integer.</returns>
        public int Next()
        {
            return _random.Next();
            
        }

        /// <summary>
        /// Return next random not greater than max.
        /// </summary>
        /// <param name="max">max value</param>
        /// <returns>Random integer.</returns>
        public int Next(int max)
        {
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
        /// <returns>Created instance of PRNG or null if name was not found in the registered PRNG.</returns>
        private static Random CreateInstance(string name, int seed)
        {
            foreach (Tuple<string, Type> x in _registeredPRNG)
            {
                if (x.Item1 == name)
                    return (Random)Activator.CreateInstance(x.Item2, seed);
            }

            return null;
        }

        /// <summary>
        /// Allows user to register its own PRNG.
        /// Every registered algorithm must derive from Random class.
        /// </summary>
        /// <param name="name">Name of the PRNG.</param>
        /// <param name="creator">Type to be used for PRNG creation.</param>
        /// <returns>True if successful.</returns>
        public static bool RegisterPRNG(string name, Type creator)
        {
            if (!creator.IsSubclassOf(typeof(Random)) && creator != typeof(Random))
                return false;

            _registeredPRNG.Add(new Tuple<string, Type>(name, creator));
            return true;
        }

        /// <summary>
        /// List with currently registered PRNG.
        /// </summary>
        private static List<Tuple<string, Type>> _registeredPRNG = new List<Tuple<string, Type>>
                (new[] {
            new Tuple<string, Type>( "Random", typeof(Random)),
        });
    }
}
