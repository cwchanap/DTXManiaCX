using System.Collections.Generic;
using DTXMania.Shared.Game;

namespace DTX.Stage
{
    public class StageManager : IStageManager
    {
        private readonly BaseGame _game;
        private readonly Dictionary<StageType, IStage> _stages;
        private IStage _currentStage;

        public IStage CurrentStage => _currentStage;

        public StageManager(BaseGame game)
        {
            _game = game;
            _stages = new Dictionary<StageType, IStage>();
            InitializeStages();
        }

        private void InitializeStages()
        {
            // For Phase 1, only implement Startup stage
            _stages[StageType.Startup] = new StartupStage(_game);
        }

        public void ChangeStage(StageType stageType)
        {
            _currentStage?.Deactivate();

            if (_stages.TryGetValue(stageType, out var stage))
            {
                _currentStage = stage;
                _currentStage.Activate();
            }
        }

        public void Update(double deltaTime)
        {
            _currentStage?.Update(deltaTime);
        }

        public void Draw(double deltaTime)
        {
            _currentStage?.Draw(deltaTime);
        }
    }
}