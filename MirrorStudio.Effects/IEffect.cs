namespace MirrorStudio.Effects
{
    public interface IEffect
    {
        string Name { get; }
        void Apply(object target);
    }
}
