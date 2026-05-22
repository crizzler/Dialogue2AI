using System.Collections.Generic;

namespace ImmersiveNPCs
{
    public sealed class LruCache<TKey, TValue>
    {
        private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> map;
        private readonly LinkedList<KeyValuePair<TKey, TValue>> list;
        private int capacity;

        public LruCache(int capacity)
        {
            this.capacity = capacity < 1 ? 1 : capacity;
            map = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>();
            list = new LinkedList<KeyValuePair<TKey, TValue>>();
        }

        public int Count => map.Count;

        public void SetCapacity(int newCapacity)
        {
            capacity = newCapacity < 1 ? 1 : newCapacity;
            TrimExcess();
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (map.TryGetValue(key, out var node))
            {
                list.Remove(node);
                list.AddFirst(node);
                value = node.Value.Value;
                return true;
            }

            value = default;
            return false;
        }

        public void AddOrUpdate(TKey key, TValue value)
        {
            if (map.TryGetValue(key, out var node))
            {
                node.Value = new KeyValuePair<TKey, TValue>(key, value);
                list.Remove(node);
                list.AddFirst(node);
            }
            else
            {
                var newNode = new LinkedListNode<KeyValuePair<TKey, TValue>>(new KeyValuePair<TKey, TValue>(key, value));
                list.AddFirst(newNode);
                map[key] = newNode;
                TrimExcess();
            }
        }

        public bool Remove(TKey key)
        {
            if (map.TryGetValue(key, out var node))
            {
                list.Remove(node);
                return map.Remove(key);
            }

            return false;
        }

        private void TrimExcess()
        {
            while (map.Count > capacity)
            {
                var last = list.Last;
                if (last == null)
                {
                    break;
                }
                map.Remove(last.Value.Key);
                list.RemoveLast();
            }
        }
    }
}
