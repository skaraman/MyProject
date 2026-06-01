using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CustomInspector
{
    /// <summary>
    /// Only valid for ArrayContainer! Used to fix overriding of other attributes
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public class ArrayContainerAttribute : ComparablePropertyAttribute
    {
        protected override object[] GetParameters() => null;
    }

    /// <summary>
    /// An equivalent to the System.Collections.Generic.List<T>
    /// </summary>
    [System.Serializable]
    public class ArrayContainer<T> : ICollection<T>, IEnumerable<T>, IList<T>, IReadOnlyCollection<T>, IReadOnlyList<T>,
        IEnumerable, ICollection, IList,
        IStructuralComparable, IStructuralEquatable, ICloneable
    {
        [MessageBox("ArrayContainer is missing the [ArrayContainer]-attribute to be correctly displayed")]
        [SerializeField, HideField] bool _;

        // CONTENT
        [SerializeField]
        T[] values = new T[0];


        // CONSTRUCTORS
        public ArrayContainer() { }
        public ArrayContainer(int size) : base()
        {
            values = new T[size];
        }
        public ArrayContainer(T[] array) : base()
        {
            if (array == null)
                throw new ArgumentNullException($"Provided {nameof(array)} is null.");
            values = array;
        }
        public ArrayContainer(System.Array array) : base()
        {
            if (array == null)
                throw new ArgumentNullException($"Provided {nameof(array)} is null.");
            values = array.Cast<T>().ToArray();
        }
        public ArrayContainer(IEnumerable<T> collection) : base()
        {
            if (collection == null)
                throw new ArgumentNullException($"Provided {nameof(collection)} is null.");
            values = collection.ToArray();
        }

        // COMPATIBILITY
        public static implicit operator ArrayContainer<T>(T[] array)
        {
            if (array != null)
                return new ArrayContainer<T>(array);
            return null;
        }
        public static implicit operator T[](ArrayContainer<T> container) => container?.values;

        public static implicit operator ArrayContainer<T>(System.Array array)
        {
            if (array != null)
                return new ArrayContainer<T>(array);
            return null;
        }
        public static implicit operator System.Array(ArrayContainer<T> container) => container?.values;

        // INDEXER
        public T this[int index]
        {
            get => values[index];
            set => values[index] = value;
        }

        object IList.this[int index]
        {
            get => values[index]!;
            set => values[index] = (T)value;
        }



        // --- FORWARD ACCESS TO System.Array: https://learn.microsoft.com/de-de/dotnet/api/system.array?view=net-9.0 ---

#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
        // PROPERTIES
        /// <summary>True because arrays have fixed size.</summary>
        public bool IsFixedSize => values.IsFixedSize;

        /// <summary>True if the container is read-only. Standard arrays are not read-only for element access.</summary>
        public bool IsReadOnly => values.IsReadOnly;

        /// <summary>Always false for managed arrays unless explicitly synchronized externally.</summary>
        public bool IsSynchronized => ((ICollection)values).IsSynchronized;

        /// <summary>Number of elements in the array.</summary>
        public int Length => values.Length;

        /// <summary>Number of elements as a long (supports very large arrays).</summary>
        public long LongLength => values.LongLength;

        /// <summary>Maximum allowed array length. Uses the internal limit of System.Array.</summary>
        public int MaxLength => int.MaxValue; // equivalent to System.Array.MaxLength in .NET Core/Standard

        /// <summary>Number of dimensions. Always 1 for T[].</summary>
        public int Rank => values.Rank;

        /// <summary>Object used for thread synchronization.</summary>
        public object SyncRoot => ((ICollection)values).SyncRoot;

        // METHODS
        // Returns a read-only wrapper around a given array
        public static IReadOnlyList<TItem> AsReadOnly<TItem>(TItem[] array)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            return Array.AsReadOnly(array);
        }

        // BinarySearch methods (non-generic overloads)
        public static int BinarySearch(Array array, int index, int length, object value)
        => Array.BinarySearch(array, index, length, value);

        public static int BinarySearch(Array array, int index, int length, object value, IComparer comparer)
        => Array.BinarySearch(array, index, length, value, comparer);

        public static int BinarySearch(Array array, object value)
        => Array.BinarySearch(array, value);

        public static int BinarySearch(Array array, object value, IComparer comparer)
        => Array.BinarySearch(array, value, comparer);

        // BinarySearch methods (generic overloads)
        public static int BinarySearch<TItem>(TItem[] array, int index, int length, TItem value)
        => Array.BinarySearch(array, index, length, value);

        public static int BinarySearch<TItem>(TItem[] array, int index, int length, TItem value, IComparer<TItem> comparer)
        => Array.BinarySearch(array, index, length, value, comparer);

        public static int BinarySearch<TItem>(TItem[] array, TItem value)
        => Array.BinarySearch(array, value);

        public static int BinarySearch<TItem>(TItem[] array, TItem value, IComparer<TItem> comparer)
        => Array.BinarySearch(array, value, comparer);
        // ---------- ARRAY OPERATIONS ----------

        // Clear
        public void Clear() => Array.Clear(values, 0, values.Length);
        public void Clear(int index, int length) => Array.Clear(values, index, length);
        public object Clone() => values.Clone();

        // ConstrainedCopy
        public static void ConstrainedCopy(Array sourceArray, int sourceIndex, Array destinationArray, int destinationIndex, int length)
            => Array.ConstrainedCopy(sourceArray, sourceIndex, destinationArray, destinationIndex, length);

        // ConvertAll
        public static TOutput[] ConvertAll<TInput, TOutput>(TInput[] array, Converter<TInput, TOutput> converter)
            => Array.ConvertAll(array, converter!);

        // Copy (overloads)
        public static void Copy(Array sourceArray, Array destinationArray, int length)
            => Array.Copy(sourceArray, destinationArray, length);

        public static void Copy(Array sourceArray, Array destinationArray, long length)
            => Array.Copy(sourceArray, destinationArray, length);

        public static void Copy(Array sourceArray, int sourceIndex, Array destinationArray, int destinationIndex, int length)
            => Array.Copy(sourceArray, sourceIndex, destinationArray, destinationIndex, length);

        public static void Copy(Array sourceArray, long sourceIndex, Array destinationArray, long destinationIndex, long length)
            => Array.Copy(sourceArray, sourceIndex, destinationArray, destinationIndex, length);

        // CopyTo instance forwarding
        public void CopyTo(Array array, int index) => values.CopyTo(array, index);
        public void CopyTo(Array array, long index) => Array.Copy(values, 0, array, index, values.Length);

        // ---------- ARRAY FACTORIES ----------

        // Standard CreateInstance overloads
        public static Array CreateInstance(Type elementType, int length)
            => Array.CreateInstance(elementType, length);

        public static Array CreateInstance(Type elementType, int length1, int length2)
            => Array.CreateInstance(elementType, length1, length2);

        public static Array CreateInstance(Type elementType, int length1, int length2, int length3)
            => Array.CreateInstance(elementType, length1, length2, length3);

        public static Array CreateInstance(Type elementType, int[] lengths)
            => Array.CreateInstance(elementType, lengths);

        public static Array CreateInstance(Type elementType, int[] lengths, int[] lowerBounds)
            => Array.CreateInstance(elementType, lengths, lowerBounds);

        public static Array CreateInstance(Type elementType, long[] lengths)
            => Array.CreateInstance(elementType, lengths);

        // CreateInstance from array type - only available since .NET 9
        /*        public static Array CreateInstanceFromArrayType(Type arrayType, int length)
                    => Array.CreateInstanceFromArrayType(arrayType, length);

                public static Array CreateInstanceFromArrayType(Type arrayType, int[] lengths)
                    => Array.CreateInstanceFromArrayType(arrayType, lengths);

                public static Array CreateInstanceFromArrayType(Type arrayType, int[] lengths, int[] lowerBounds)
                    => Array.CreateInstanceFromArrayType(arrayType, lengths, lowerBounds);*/

        // Empty array shortcut
        public static T[] Empty() => Array.Empty<T>();

        // ---------- ARRAY SEARCH & UTILITIES ----------

        // Equals override
        public override bool Equals(object? obj) => values.Equals(obj);

        // Exists
        public static bool Exists<TItem>(TItem[] array, Predicate<TItem> match)
            => Array.Exists(array, match);

        // Fill
        public static void Fill<TItem>(TItem[] array, TItem value)
            => Array.Fill(array, value);

        public static void Fill<TItem>(TItem[] array, TItem value, int startIndex, int count)
            => Array.Fill(array, value, startIndex, count);

        // Find
        public static TItem? Find<TItem>(TItem[] array, Predicate<TItem> match)
            => Array.Find(array, match);

        // FindAll
        public static TItem[] FindAll<TItem>(TItem[] array, Predicate<TItem> match)
            => Array.FindAll(array, match);

        // FindIndex
        public static int FindIndex<TItem>(TItem[] array, int startIndex, int count, Predicate<TItem> match)
            => Array.FindIndex(array, startIndex, count, match);

        public static int FindIndex<TItem>(TItem[] array, int startIndex, Predicate<TItem> match)
            => Array.FindIndex(array, startIndex, match);

        public static int FindIndex<TItem>(TItem[] array, Predicate<TItem> match)
            => Array.FindIndex(array, match);

        // FindLast
        public static TItem? FindLast<TItem>(TItem[] array, Predicate<TItem> match)
            => Array.FindLast(array, match);

        // FindLastIndex
        public static int FindLastIndex<TItem>(TItem[] array, int startIndex, int count, Predicate<TItem> match)
            => Array.FindLastIndex(array, startIndex, count, match);

        public static int FindLastIndex<TItem>(TItem[] array, int startIndex, Predicate<TItem> match)
            => Array.FindLastIndex(array, startIndex, match);

        public static int FindLastIndex<TItem>(TItem[] array, Predicate<TItem> match)
            => Array.FindLastIndex(array, match);

        // ForEach
        public static void ForEach<TItem>(TItem[] array, Action<TItem> action)
            => Array.ForEach(array, action);

        // ---------- ARRAY INSTANCE METHODS ----------

        // Enumeration
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)values).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)values).GetEnumerator();

        // Hash code
        public override int GetHashCode() => values.GetHashCode();

        // Lengths
        public int GetLength(int dimension) => values.GetLength(dimension);
        public long GetLongLength(int dimension) => values.GetLongLength(dimension);

        // Bounds
        public int GetLowerBound(int dimension) => values.GetLowerBound(dimension);
        public int GetUpperBound(int dimension) => values.GetUpperBound(dimension);

        // Type
        public new Type GetType() => values.GetType();

        // Value access by int index
        public T GetValue(int index) => values.GetValue(index) is T v ? v : default!;
        public T GetValue(int index1, int index2) => values.GetValue(index1, index2) is T v ? v : default!;
        public T GetValue(int index1, int index2, int index3) => values.GetValue(index1, index2, index3) is T v ? v : default!;
        public T GetValue(int[] indices) => values.GetValue(indices) is T v ? v : default!;

        // Value access by long index
        public T GetValue(long index) => values.GetValue(index) is T v ? v : default!;
        public T GetValue(long index1, long index2) => values.GetValue(index1, index2) is T v ? v : default!;
        public T GetValue(long index1, long index2, long index3) => values.GetValue(index1, index2, index3) is T v ? v : default!;
        public T GetValue(long[] indices) => values.GetValue(indices) is T v ? v : default!;

        // ---------- ARRAY SEARCH & INITIALIZE ----------

        // IndexOf (non-generic)
        public static int IndexOf(Array array, object value)
            => Array.IndexOf(array, value);
        public static int IndexOf(Array array, object value, int startIndex)
            => Array.IndexOf(array, value, startIndex);
        public static int IndexOf(Array array, object value, int startIndex, int count)
            => Array.IndexOf(array, value, startIndex, count);

        // IndexOf (generic)
        public static int IndexOf<TItem>(TItem[] array, TItem value)
            => Array.IndexOf(array, value);
        public static int IndexOf<TItem>(TItem[] array, TItem value, int startIndex)
            => Array.IndexOf(array, value, startIndex);
        public static int IndexOf<TItem>(TItem[] array, TItem value, int startIndex, int count)
            => Array.IndexOf(array, value, startIndex, count);

        // Initialize
        public void Initialize() => Array.Clear(values, 0, values.Length);

        // LastIndexOf (non-generic)
        public static int LastIndexOf(Array array, object value)
            => Array.LastIndexOf(array, value);
        public static int LastIndexOf(Array array, object value, int startIndex)
            => Array.LastIndexOf(array, value, startIndex);
        public static int LastIndexOf(Array array, object value, int startIndex, int count)
            => Array.LastIndexOf(array, value, startIndex, count);

        // LastIndexOf (generic)
        public static int LastIndexOf<TItem>(TItem[] array, TItem value)
            => Array.LastIndexOf(array, value);
        public static int LastIndexOf<TItem>(TItem[] array, TItem value, int startIndex)
            => Array.LastIndexOf(array, value, startIndex);
        public static int LastIndexOf<TItem>(TItem[] array, TItem value, int startIndex, int count)
            => Array.LastIndexOf(array, value, startIndex, count);

        // ---------- ARRAY CLONE, RESIZE, REVERSE, SETVALUE ----------

        // Clone
        public new object MemberwiseClone() => base.MemberwiseClone();

        // Resize
        public static void Resize<TItem>(ref TItem[] array, int newSize) => Array.Resize(ref array, newSize);

        // Reverse (non-generic)
        public static void Reverse(Array array) => Array.Reverse(array);
        public static void Reverse(Array array, int index, int length) => Array.Reverse(array, index, length);

        // Reverse (generic)
        public static void Reverse<TItem>(TItem[] array) => Array.Reverse(array);
        public static void Reverse<TItem>(TItem[] array, int index, int length) => Array.Reverse(array, index, length);

        // SetValue (int indices)
        public void SetValue(object value, int index) => values.SetValue(value, index);
        public void SetValue(object value, int index1, int index2) => values.SetValue(value, index1, index2);
        public void SetValue(object value, int index1, int index2, int index3) => values.SetValue(value, index1, index2, index3);
        public void SetValue(object value, int[] indices) => values.SetValue(value, indices);

        // SetValue (long indices)
        public void SetValue(object value, long index) => values.SetValue(value, index);
        public void SetValue(object value, long index1, long index2) => values.SetValue(value, index1, index2);
        public void SetValue(object value, long index1, long index2, long index3) => values.SetValue(value, index1, index2, index3);
        public void SetValue(object value, long[] indices) => values.SetValue(value, indices);

        // ---------- ARRAY SORT & UTILITY ----------

        // Sort (non-generic)
        public static void Sort(Array array) => Array.Sort(array);
        public static void Sort(Array keys, Array items) => Array.Sort(keys, items);
        public static void Sort(Array keys, Array items, IComparer comparer) => Array.Sort(keys, items, comparer);
        public static void Sort(Array keys, Array items, int index, int length) => Array.Sort(keys, items, index, length);
        public static void Sort(Array keys, Array items, int index, int length, IComparer comparer) => Array.Sort(keys, items, index, length, comparer);
        public static void Sort(Array array, IComparer comparer) => Array.Sort(array, comparer);
        public static void Sort(Array array, int index, int length) => Array.Sort(array, index, length);
        public static void Sort(Array array, int index, int length, IComparer comparer) => Array.Sort(array, index, length, comparer);

        // Sort (generic)
        public static void Sort<TItem>(TItem[] array) => Array.Sort(array);
        public static void Sort<TItem>(TItem[] array, Comparison<TItem> comparison) => Array.Sort(array, comparison);
        public static void Sort<TItem>(TItem[] array, IComparer<TItem> comparer) => Array.Sort(array, comparer);
        public static void Sort<TItem>(TItem[] array, int index, int length) => Array.Sort(array, index, length);
        public static void Sort<TItem>(TItem[] array, int index, int length, IComparer<TItem> comparer) => Array.Sort(array, index, length, comparer);

        // Sort by key/value
        public static void Sort<TKey, TValue>(TKey[] keys, TValue[] items) => Array.Sort(keys, items);
        public static void Sort<TKey, TValue>(TKey[] keys, TValue[] items, IComparer<TKey> comparer) => Array.Sort(keys, items, comparer);
        public static void Sort<TKey, TValue>(TKey[] keys, TValue[] items, int index, int length) => Array.Sort(keys, items, index, length);
        public static void Sort<TKey, TValue>(TKey[] keys, TValue[] items, int index, int length, IComparer<TKey> comparer)
            => Array.Sort(keys, items, index, length, comparer);

        // ToString override
        public override string ToString() => values.ToString();

        // TrueForAll
        public static bool TrueForAll<TItem>(TItem[] array, Predicate<TItem> match) => Array.TrueForAll(array, match);


        // --- MISSING INTERFACES ---
        //public int Length => values.Length;
        //public long LongLength => values.LongLength;
        //public int Rank => values.Rank;
        //public bool IsFixedSize => values.IsFixedSize;
        //public bool IsReadOnly => false;
        //public bool IsSynchronized => ((ICollection)values).IsSynchronized;
        //public object SyncRoot => ((ICollection)values).SyncRoot;

        // METHODS FROM System.Array
        //public void CopyTo(Array array, int index) => values.CopyTo(array, index);
        public void CopyTo(T[] array, int index) => values.CopyTo(array, index);
        //public object Clone() => values.Clone();
        //public T GetValue(int index) => (T)values.GetValue(index)!;
        public void SetValue(T value, int index) => values.SetValue(value, index);

        // STATIC UTILS FROM Array
        public static void Sort(T[] array) => Array.Sort(array);
        public static void Reverse(T[] array) => Array.Reverse(array);
        public static int IndexOf(T[] array, T value) => Array.IndexOf(array, value);
        public static int LastIndexOf(T[] array, T value) => Array.LastIndexOf(array, value);
        public static T[] CreateInstance(int length) => (T[])Array.CreateInstance(typeof(T), length);

        // GENERIC IList<T> IMPLEMENTATION
        public int Count => values.Length;

        public int IndexOf(T item) => Array.IndexOf(values, item);
        public void Insert(int index, T item) => throw new NotSupportedException("Array has fixed size");
        public void RemoveAt(int index) => throw new NotSupportedException("Array has fixed size");
        public void Add(T item) => throw new NotSupportedException("Array has fixed size");
        //public void Clear() => Array.Clear(values, 0, values.Length);
        public bool Contains(T item) => Array.IndexOf(values, item) >= 0;
        public bool Remove(T item) => throw new NotSupportedException("Array has fixed size");

        // NON-GENERIC IList
        public int Add(object? value) => throw new NotSupportedException("Array has fixed size");
        public bool Contains(object? value) => ((IList)values).Contains(value);
        public int IndexOf(object? value) => ((IList)values).IndexOf(value);
        public void Insert(int index, object? value) => throw new NotSupportedException("Array has fixed size");
        public void Remove(object? value) => throw new NotSupportedException("Array has fixed size");

        // IStructuralComparable
        public int CompareTo(object other, IComparer comparer) => ((IStructuralComparable)values).CompareTo(other, comparer);
        public bool Equals(object other, IEqualityComparer comparer) => ((IStructuralEquatable)values).Equals(other, comparer);

        // IStructuralEquatable
        public int GetHashCode(IEqualityComparer comparer) => ((IStructuralEquatable)values).GetHashCode(comparer);
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
    }
}
