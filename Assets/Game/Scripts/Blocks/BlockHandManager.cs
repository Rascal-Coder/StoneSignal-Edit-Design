using System.Collections.Generic;
namespace StoneSignal {
 /// Wall-block hand. Each card is a shape plus an optional rune (RuneRules.NoRune = none) inlaid on the shape's first cell.
 /// Identical cards (same shape + same rune) are shown as one stacked card with a xN count (Groups()).
 public sealed class BlockHandManager {
  private readonly List<BlockShapeData> cards=new List<BlockShapeData>();
  private readonly List<int> runes=new List<int>();
  public const int MaxCards=7; // hand carries across waves, capped
  public bool IsFull=>cards.Count>=MaxCards;
  public IReadOnlyList<BlockShapeData> Cards=>cards;
  public IReadOnlyList<int> Runes=>runes;
  public int Selected {get;private set;}
  public BlockShapeData Current=>cards.Count==0?null:cards[Selected];
  public int CurrentRune=>cards.Count==0?RuneRules.NoRune:runes[Selected];
  /// Replace the hand (run start).
  public void Draw(BlockDeckManager deck,int count,System.Func<int> rollRune=null) { cards.Clear(); runes.Clear(); Selected=0; Add(deck,count,rollRune); }
  /// Append drawn cards (DRAW pile during an intermission).
  public void Add(BlockDeckManager deck,int count,System.Func<int> rollRune=null) { for(int i=0;i<count&&cards.Count<MaxCards;i++) { var card=deck.Draw(); if(card!=null) { cards.Add(card); runes.Add(rollRune!=null?rollRune():RuneRules.NoRune); } } }
  public void AddCard(BlockShapeData shape,int rune) { if(shape!=null&&cards.Count<MaxCards) { cards.Add(shape); runes.Add(rune); } }
  public bool Select(int i) { if(i<0||i>=cards.Count) return false; Selected=i; return true; }
  public void Consume() { if(cards.Count==0)return; cards.RemoveAt(Selected); runes.RemoveAt(Selected); Selected=System.Math.Min(Selected,System.Math.Max(0,cards.Count-1)); }
  public struct Group { public BlockShapeData shape; public int rune, first, count; }
  /// Stacks identical cards in first-seen order.
  public List<Group> Groups() {
   var list=new List<Group>();
   for(int i=0;i<cards.Count;i++) {
    int g=list.FindIndex(x=>x.shape==cards[i]&&x.rune==runes[i]);
    if(g<0) list.Add(new Group{shape=cards[i],rune=runes[i],first=i,count=1}); else { var x=list[g]; x.count++; list[g]=x; }
   }
   return list;
  }
 }
}
