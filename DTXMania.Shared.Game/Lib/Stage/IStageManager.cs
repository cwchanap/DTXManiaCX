using System;

namespace DTX.Stage
{
    public interface IStageManager : IDisposable
    {
        IStage CurrentStage { get; }
        void ChangeStage(StageType stageType);
        void Update(double deltaTime);
        void Draw(double deltaTime);
    }

    public enum StageType
    {
        Startup,
        Title,
        Config,
        SongSelect,
        Performance,
        Result,
        UITest  // For testing UI components
    }

    public interface IStage : IDisposable
    {
        StageType Type { get; }
        void Activate();
        void Deactivate();
        void Update(double deltaTime);
        void Draw(double deltaTime);
    }
}