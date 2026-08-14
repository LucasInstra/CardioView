using System;
using System.Collections;
using System.Collections.Generic;

namespace CardioView.Simulation;

/// <summary>
/// Buffer circular de tamanho fixo que descarta automaticamente os itens mais
/// antigos ao exceder a capacidade — O(1) por inserção, sem deslocamento de
/// elementos como em List.RemoveRange(0, ...).
/// </summary>
public sealed class RingBuffer<T> : IReadOnlyList<T>
{
    private readonly T[] _items;
    private readonly int _capacity;
    private int _head;
    private int _count;

    public RingBuffer(int capacity)
    {
        _capacity = Math.Max(1, capacity);
        _items = new T[_capacity];
    }

    public int Count => _count;

    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= _count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _items[(_head + index) % _capacity];
        }
    }

    public void Add(T item)
    {
        if (_count < _capacity)
        {
            _items[(_head + _count) % _capacity] = item;
            _count++;
        }
        else
        {
            _items[_head] = item;
            _head = (_head + 1) % _capacity;
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
