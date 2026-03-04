using System;
using System.Collections.ObjectModel;
using WebRtcPhoneDialer.Core.Models;
using WebRtcPhoneDialer.Core.Services;

namespace WebRtcPhoneDialer.ViewModels
{
    public class MainWindowViewModel : BaseViewModel
    {
        private readonly WebRtcService _webRtcService;
        private readonly CallHistoryService _callHistoryService = new CallHistoryService();
        private string _phoneNumber = string.Empty;
        private string _callStatus = "Ready";
        private bool _isCallActive = false;

        public ObservableCollection<CallSession> CallHistory { get; } = new ObservableCollection<CallSession>();

        public void AddCallToHistory(CallSession call)
        {
            _callHistoryService.AddCall(call);
            CallHistory.Insert(0, call); // newest first
        }

        public void ClearCallHistory()
        {
            _callHistoryService.ClearHistory();
            CallHistory.Clear();
        }

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
