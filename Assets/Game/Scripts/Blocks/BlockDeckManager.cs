using System.Collections.Generic;
using UnityEngine;
namespace StoneSignal {
 public sealed class BlockDeckManager {
  private readonly List<BlockShapeData> cards = new List<BlockShapeData>();
  private readonly List<BlockShapeData> draw = new List<BlockShapeData>();
  public IReadOnlyList<BlockShapeData> Cards => cards;
  public BlockDeckManager(IEnumerable<BlockShapeData> initial) { cards.AddRange(initial); }
  public void Add(BlockShapeData shape) { if(shape != null) { cards.Add(shape); draw.Add(shape); } }
  public BlockShapeData Draw() {
   if(draw.Count==0) draw.AddRange(cards);
   if(draw.Count==0) return null;
   int i=Random.Range(0,draw.Count); var card=draw[i]; draw.RemoveAt(i); return card;
  }
 }
}