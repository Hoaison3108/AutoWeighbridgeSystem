using System;
using System.ComponentModel;
using System.Windows.Threading;

namespace AutoWeighbridgeSystem.Services
{
    public class SystemClockService : INotifyPropertyChanged
    {
        private DateTime _currentTime;
        private readonly DispatcherTimer _timer;

        public DateTime CurrentTime
        {
            get => _currentTime;
            private set
            {
                if (_currentTime != value)
                {
                    _currentTime = value;
                    OnPropertyChanged(nameof(CurrentTime));
                }
            }
        }

        public SystemClockService()
        {
            _currentTime = DateTime.Now;
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (s, e) => CurrentTime = DateTime.Now;
            _timer.Start();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
