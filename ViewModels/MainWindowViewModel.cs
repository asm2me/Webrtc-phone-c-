using System.Collections.ObjectModel;
using WebRtcPhoneDialer.Services;

namespace WebRtcPhoneDialer.ViewModels
{
    public class MainWindowViewModel : BaseViewModel
    {
        private readonly WebRtcService _webRtcService;
        private string _phoneNumber = string.Empty;
        private string _callStatus = "Ready";
        private bool _isCallActive = false;

        public ObservableCollection<string> CallHistory { get; } = new ObservableCollection<string>();

        public string PhoneNumber
        {
            get => _phoneNumber;
            set
            {
                if (_phoneNumber != value)
                {
                    _phoneNumber = value;
                    OnPropertyChanged(nameof(PhoneNumber));
                }
            }
        }

        public string CallStatus
        {
            get => _callStatus;
            set
            {
                if (_callStatus != value)
                {
                    _callStatus = value;
                    OnPropertyChanged(nameof(CallStatus));
                }
            }
        }

        public bool IsCallActive
        {
            get => _isCallActive;
            set
            {
                if (_isCallActive != value)
                {
                    _isCallActive = value;
                    OnPropertyChanged(nameof(IsCallActive));
                }
            }
        }

        public MainWindowViewModel(WebRtcService webRtcService)
        {
            _webRtcService = webRtcService;
        }
    }
}
