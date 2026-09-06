using System;

namespace NotRealGames.Areafinder
{
    internal sealed class SearchMinHeap
    {
        private readonly int[] _heap;
        private readonly int[] _positions;
        private readonly float[] _priorities;
        private readonly int[] _tieBreakers;
        private int _count;

        internal SearchMinHeap(int nodeCapacity)
        {
            if (nodeCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeCapacity));
            }

            _heap = new int[nodeCapacity];
            _positions = new int[nodeCapacity];
            _priorities = new float[nodeCapacity];
            _tieBreakers = new int[nodeCapacity];
            Clear();
        }

        internal int Count => _count;

        internal void Clear()
        {
            _count = 0;
            Array.Fill(_positions, -1);
        }

        internal void PushOrDecrease(int node, float priority, int tieBreaker)
        {
            if ((uint)node >= (uint)_positions.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(node));
            }

            if (float.IsNaN(priority) || float.IsInfinity(priority))
            {
                throw new ArgumentOutOfRangeException(nameof(priority));
            }

            int position = _positions[node];
            if (position >= 0)
            {
                if (!ComesBefore(priority, tieBreaker, node, _priorities[node], _tieBreakers[node], node))
                {
                    return;
                }

                _priorities[node] = priority;
                _tieBreakers[node] = tieBreaker;
                SiftUp(position);
                return;
            }

            if (_count == _heap.Length)
            {
                throw new InvalidOperationException("The search heap capacity was exceeded.");
            }

            _heap[_count] = node;
            _positions[node] = _count;
            _priorities[node] = priority;
            _tieBreakers[node] = tieBreaker;
            SiftUp(_count);
            _count++;
        }

        internal bool TryPop(out int node, out float priority)
        {
            if (_count == 0)
            {
                node = -1;
                priority = 0f;
                return false;
            }

            node = _heap[0];
            priority = _priorities[node];
            _positions[node] = -1;
            _count--;

            if (_count > 0)
            {
                int replacement = _heap[_count];
                _heap[0] = replacement;
                _positions[replacement] = 0;
                SiftDown(0);
            }

            return true;
        }

        private void SiftUp(int position)
        {
            int node = _heap[position];
            while (position > 0)
            {
                int parent = (position - 1) / 2;
                int parentNode = _heap[parent];
                if (!ComesBefore(node, parentNode))
                {
                    break;
                }

                _heap[position] = parentNode;
                _positions[parentNode] = position;
                position = parent;
            }

            _heap[position] = node;
            _positions[node] = position;
        }

        private void SiftDown(int position)
        {
            int node = _heap[position];
            while (true)
            {
                int left = (position * 2) + 1;
                if (left >= _count)
                {
                    break;
                }

                int right = left + 1;
                int best = right < _count && ComesBefore(_heap[right], _heap[left]) ? right : left;
                int bestNode = _heap[best];
                if (!ComesBefore(bestNode, node))
                {
                    break;
                }

                _heap[position] = bestNode;
                _positions[bestNode] = position;
                position = best;
            }

            _heap[position] = node;
            _positions[node] = position;
        }

        private bool ComesBefore(int leftNode, int rightNode)
        {
            return ComesBefore(
                _priorities[leftNode],
                _tieBreakers[leftNode],
                leftNode,
                _priorities[rightNode],
                _tieBreakers[rightNode],
                rightNode);
        }

        private static bool ComesBefore(
            float leftPriority,
            int leftTieBreaker,
            int leftNode,
            float rightPriority,
            int rightTieBreaker,
            int rightNode)
        {
            int priorityComparison = leftPriority.CompareTo(rightPriority);
            if (priorityComparison != 0)
            {
                return priorityComparison < 0;
            }

            int tieComparison = leftTieBreaker.CompareTo(rightTieBreaker);
            return tieComparison != 0 ? tieComparison < 0 : leftNode < rightNode;
        }
    }
}
