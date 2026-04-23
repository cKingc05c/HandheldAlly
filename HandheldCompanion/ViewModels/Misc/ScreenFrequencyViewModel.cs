namespace HandheldCompanion.ViewModels.Misc
{
    public class ScreenFrequencyViewModel
    {
        public int Frequency { get; }

        public ScreenFrequencyViewModel(int frequency)
        {
            Frequency = frequency;
        }

        public override string ToString()
        {
            return $"{Frequency} Hz";
        }

        public override bool Equals(object? obj) => obj is ScreenFrequencyViewModel other && Frequency == other.Frequency;
        public override int GetHashCode() => Frequency.GetHashCode();
    }
}
