using System;
using System.Collections.Generic;
using DTXMania.Shared.Game;

namespace DTX.Stage
{
    public class StageManager : IStageManager
    {
        private readonly BaseGame _game;
        private readonly Dictionary<StageType, IStage> _stages;
        private IStage _currentStage;
        private bool _disposed = false;

        public IStage CurrentStage => _currentStage;

        public StageManager(BaseGame game)
        {
            _game = game;
            _stages = new Dictionary<StageType, IStage>();
            InitializeStages();
        }

        private void InitializeStages()
        {
            // Initialize all available stages
            _stages[StageType.Startup] = new StartupStage(_game);
            _stages[StageType.Title] = new TitleStage(_game);
            _stages[StageType.Config] = new ConfigStage(_game); // Placeholder for config stage
            _stages[StageType.UITest] = new UITestStage(_game); // UI test stage
        }

        public void ChangeStage(StageType stageType)
        {
            if (_disposed)
            {
                System.Diagnostics.Debug.WriteLine($"StageManager: Cannot change to {stageType} - manager is disposed");
                return;
            }

            var previousStageType = _currentStage?.Type;

            // Deactivate current stage
            if (_currentStage != null)
            {
                System.Diagnostics.Debug.WriteLine($"StageManager: Deactivating {previousStageType}");
                _currentStage.Deactivate();
            }

            // Activate new stage
            if (_stages.TryGetValue(stageType, out var stage))
            {
                System.Diagnostics.Debug.WriteLine($"StageManager: Activating {stageType}");
                _currentStage = stage;
                _currentStage.Activate();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"StageManager: Stage {stageType} not found");
                _currentStage = null;
            }
        }

        public void Update(double deltaTime)
        {
            if (_disposed)
                return;

            _currentStage?.Update(deltaTime);
        }

        public void Draw(double deltaTime)
        {
            if (_disposed)
                return;

            _currentStage?.Draw(deltaTime);
        }

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Deactivate current stage before disposal
                    _currentStage?.Deactivate();
                    _currentStage = null;

                    // Dispose all stages
                    foreach (var stage in _stages.Values)
                    {
                        stage?.Dispose();
                    }
                    _stages.Clear();
                }
                _disposed = true;
            }
        }

        #endregion
    }
}