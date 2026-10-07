using System.ComponentModel;
using System.Windows.Forms;
using System.Windows.Shell;
using Bloxstrap.UI.Elements.Bootstrapper.Base;
using Bloxstrap.UI.ViewModels.Bootstrapper;

namespace Bloxstrap.UI.Elements.Bootstrapper
{
    public partial class DepthStrapDialog : IBootstrapperDialog
    {
        private readonly BootstrapperDialogViewModel _model;
        private bool _closing;
        public Bloxstrap.Bootstrapper? Bootstrapper { get; set; }
        public DepthStrapDialog()
        {
            _model = new BootstrapperDialogViewModel(this);
            DataContext = _model;
            InitializeComponent();
        }
        private void Set(string name) => _model.OnPropertyChanged(name);
        public string Message { get => _model.Message; set { _model.Message = value; Set(nameof(_model.Message)); } }
        public ProgressBarStyle ProgressStyle { get => _model.ProgressIndeterminate ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous; set { _model.ProgressIndeterminate = value == ProgressBarStyle.Marquee; Set(nameof(_model.ProgressIndeterminate)); } }
        public int ProgressValue { get => _model.ProgressValue; set { _model.ProgressValue = value; Set(nameof(_model.ProgressValue)); } }
        public int ProgressMaximum { get => _model.ProgressMaximum; set { _model.ProgressMaximum = value; Set(nameof(_model.ProgressMaximum)); } }
        public TaskbarItemProgressState TaskbarProgressState { get => _model.TaskbarProgressState; set { _model.TaskbarProgressState = value; Set(nameof(_model.TaskbarProgressState)); } }
        public double TaskbarProgressValue { get => _model.TaskbarProgressValue; set { _model.TaskbarProgressValue = value; Set(nameof(_model.TaskbarProgressValue)); } }
        public bool CancelEnabled { get => _model.CancelEnabled; set { _model.CancelEnabled = value; Set(nameof(_model.CancelEnabled)); Set(nameof(_model.CancelButtonVisibility)); } }
        private void OnClosing(object? sender, CancelEventArgs args) { if (!_closing) Bootstrapper?.Cancel(); }
        public void ShowBootstrapper() => ShowDialog();
        public void CloseBootstrapper() { _closing = true; Dispatcher.BeginInvoke(new Action(Close)); }
        public void ShowSuccess(string message, Action? callback = null) => BaseFunctions.ShowSuccess(message, callback);
    }
}
