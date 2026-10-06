using System.Collections.Generic;
namespace StoneSignal {
 public sealed class BlockHandManager {
  private readonly List<BlockShapeData> cards=new List<BlockShapeData>();
  public IReadOnlyList<BlockShapeData> Cards=>cards;
  public int Selected {get;private set;}
  public BlockShapeData Current=>cards.Count==0?null:cards[Selected];
  public void Draw(BlockDeckManager deck,int count) { cards.Clear(); Selected=0; for(int i=0;i<count;i++) { var card=deck.Draw(); if(card!=null) cards.Add(card); } }
  public bool Select(int i) { if(i<0||i>=cards.Count) return false; Selected=i; return true; }
  public void Consume() { if(cards.Count==0)return; cards.RemoveAt(Selected); Selected=System.Math.Min(Selected,System.Math.Max(0,cards.Count-1)); }
 }
}