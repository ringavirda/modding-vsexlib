using System;
using System.Collections.Generic;

namespace ExpandedLib.Industry.Pipes;

/// <summary>
/// Sparse LU solver for the pipe-flow step matrix, which is diagonally dominant and may be unsymmetric but
/// always has a symmetric pattern. It factors without pivoting in an elimination order computed once per
/// topology: the node of least remaining degree first, so a tree eliminates leaves first with no fill and a
/// loop adds fill along its length only.
/// </summary>
internal sealed class PipeFlowSolver {
  private int _nodeCount;
  private int[] _edgeFrom = [];
  private int[] _edgeTo = [];
  private int _edgeCount;
  private bool _dirty = true;

  private int[] _diagSlot = [];
  private int[] _fromToSlot = [];
  private int[] _toFromSlot = [];
  private int[] _order = [];
  private int[][] _neighbours = [];
  private int[][] _rowSlots = [];
  private int[][] _colSlots = [];
  private int[][] _updateSlots = [];
  private double[] _values = [];

  /// <summary>How many times the elimination order has been computed.</summary>
  public int OrderBuilds { get; private set; }

  /// <summary>Declares the graph: <paramref name="edgeCount"/> edges, edge e joining <c>edgeFrom[e]</c> and <c>edgeTo[e]</c> among <paramref name="nodeCount"/> nodes. The arrays are read, not copied, and must not change until the next call.</summary>
  public void SetTopology(
    int nodeCount,
    int[] edgeFrom,
    int[] edgeTo,
    int edgeCount
  ) {
    _nodeCount = nodeCount;
    _edgeFrom = edgeFrom;
    _edgeTo = edgeTo;
    _edgeCount = edgeCount;
    _dirty = true;
  }

  /// <summary>Zeroes the matrix, rebuilding the elimination order first when the topology changed.</summary>
  public void Begin() {
    if (_dirty)
      BuildOrder();
    Array.Clear(_values);
  }

  /// <summary>Adds <paramref name="value"/> to the diagonal entry of <paramref name="node"/>.</summary>
  public void AddDiagonal(int node, double value) =>
    _values[_diagSlot[node]] += value;

  /// <summary>Adds the entries of edge <paramref name="edge"/>: <paramref name="fromRow"/> in the row of its from node, <paramref name="toRow"/> in the row of its to node.</summary>
  public void AddEdge(int edge, double fromRow, double toRow) {
    _values[_fromToSlot[edge]] += fromRow;
    _values[_toFromSlot[edge]] += toRow;
  }

  /// <summary>Factors the assembled matrix and overwrites <paramref name="rhs"/> with the solution of A x = rhs.</summary>
  public void Solve(double[] rhs) {
    for (int p = 0; p < _order.Length; p++) {
      int k = _order[p];
      int[] nb = _neighbours[k];
      int[] col = _colSlots[k];
      int[] row = _rowSlots[k];
      int[] upd = _updateSlots[k];
      double pivot = _values[_diagSlot[k]];
      int m = nb.Length;
      for (int a = 0; a < m; a++) {
        double l = _values[col[a]] / pivot;
        _values[col[a]] = l;
        rhs[nb[a]] -= l * rhs[k];
        for (int b = 0; b < m; b++)
          _values[upd[a * m + b]] -= l * _values[row[b]];
      }
    }
    for (int p = _order.Length - 1; p >= 0; p--) {
      int k = _order[p];
      int[] nb = _neighbours[k];
      int[] row = _rowSlots[k];
      double s = rhs[k];
      for (int b = 0; b < nb.Length; b++)
        s -= _values[row[b]] * rhs[nb[b]];
      rhs[k] = s / _values[_diagSlot[k]];
    }
  }

  private void BuildOrder() {
    OrderBuilds++;
    int n = _nodeCount;
    var adjacency = new HashSet<int>[n];
    for (int i = 0; i < n; i++)
      adjacency[i] = [];
    for (int e = 0; e < _edgeCount; e++) {
      if (_edgeFrom[e] == _edgeTo[e])
        continue;
      adjacency[_edgeFrom[e]].Add(_edgeTo[e]);
      adjacency[_edgeTo[e]].Add(_edgeFrom[e]);
    }

    var slots = new Dictionary<long, int>();
    int slotCount = 0;
    int Slot(int row, int col) {
      long key = (long)row * n + col;
      if (!slots.TryGetValue(key, out int s))
        slots[key] = s = slotCount++;
      return s;
    }

    _diagSlot = new int[n];
    for (int i = 0; i < n; i++)
      _diagSlot[i] = Slot(i, i);
    _fromToSlot = new int[_edgeCount];
    _toFromSlot = new int[_edgeCount];
    for (int e = 0; e < _edgeCount; e++) {
      _fromToSlot[e] = Slot(_edgeFrom[e], _edgeTo[e]);
      _toFromSlot[e] = Slot(_edgeTo[e], _edgeFrom[e]);
    }

    var byDegree = new SortedSet<(int Degree, int Node)>();
    for (int i = 0; i < n; i++)
      byDegree.Add((adjacency[i].Count, i));

    _order = new int[n];
    _neighbours = new int[n][];
    _rowSlots = new int[n][];
    _colSlots = new int[n][];
    _updateSlots = new int[n][];
    for (int p = 0; p < n; p++) {
      (int _, int k) = byDegree.Min;
      byDegree.Remove(byDegree.Min);
      _order[p] = k;
      int[] nb = [.. adjacency[k]];
      Array.Sort(nb);
      foreach (int i in nb)
        byDegree.Remove((adjacency[i].Count, i));
      foreach (int i in nb) {
        adjacency[i].Remove(k);
        foreach (int j in nb)
          if (i != j)
            adjacency[i].Add(j);
      }
      foreach (int i in nb)
        byDegree.Add((adjacency[i].Count, i));

      int m = nb.Length;
      var row = new int[m];
      var col = new int[m];
      var upd = new int[m * m];
      for (int a = 0; a < m; a++) {
        row[a] = Slot(k, nb[a]);
        col[a] = Slot(nb[a], k);
        for (int b = 0; b < m; b++)
          upd[a * m + b] = Slot(nb[a], nb[b]);
      }
      _neighbours[k] = nb;
      _rowSlots[k] = row;
      _colSlots[k] = col;
      _updateSlots[k] = upd;
    }

    _values = new double[slotCount];
    _dirty = false;
  }
}
