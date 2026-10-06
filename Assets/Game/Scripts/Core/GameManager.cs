using System;
using UnityEngine;

namespace StoneSignal
{
    public enum GameState { Build, Combat, Reward, GameOver }
    public sealed class GameManager : MonoBehaviour
    {
        private RunEconomy economy;
        public GameState State { get; private set; } = GameState.Build;
        public event Action<GameState> StateChanged;
        public void Initialize(RunEconomy resources) { economy = resources; economy.Changed += CheckBase; }
        public void SetState(GameState next)
        {
            if (State == next || State == GameState.GameOver) return;
            bool allowed = next == GameState.GameOver || (State == GameState.Build && next == GameState.Combat) || (State == GameState.Combat && next == GameState.Reward) || (State == GameState.Reward && next == GameState.Build);
            if (!allowed) throw new InvalidOperationException("Invalid state transition: " + State + " -> " + next);
            State = next; StateChanged?.Invoke(next);
        }
        private void CheckBase() { if (economy.HP <= 0) SetState(GameState.GameOver); }
        private void OnDestroy() { if (economy != null) economy.Changed -= CheckBase; }
    }
}
