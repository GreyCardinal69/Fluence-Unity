using System.Collections.Generic;

namespace Fluence.Unity.VirtualMachine
{
    /// <summary>
    /// An object pool designed to minimize Garbage Collection 
    /// allocations for short-lived, frequently created runtime objects.
    /// </summary>
    internal sealed class ObjectPool<T> where T : class, new()
    {
        private readonly Stack<T> _pool = new Stack<T>();

        /// <summary>
        /// Initializes a new instance of the <see cref="ObjectPool{T}"/> with a default capacity of 16 unless a custom capacity is given.
        /// </summary>
        /// <param name="resetAction">An optional action invoked on an object when it is returned to the pool to clear its state.</param>
        /// <param name="initialCapacity">The number of objects to pre-allocate. Defaults to 16.</param>
        internal ObjectPool(int initialCapacity = 16)
        {
            for (int i = 0; i < initialCapacity; i++)
            {
                _pool.Push(new T());
            }
        }

        /// <summary>
        /// Gets an object from the pool. If the pool is empty, a new object is created.
        /// </summary>
        internal T Get()
        {
            if (_pool.TryPop(out T? item))
            {
                return item;
            }
            return new T();
        }

        /// <summary>
        /// Returns an object to the pool, invoking the reset action if one was provided.
        /// </summary>
        /// <param name="item">The object to recycle.</param>
        internal void Return(T item)
        {
            _pool.Push(item);
        }

        /// <summary>Empties the pool.</summary>
        internal void Clear() => _pool.Clear();
    }
}