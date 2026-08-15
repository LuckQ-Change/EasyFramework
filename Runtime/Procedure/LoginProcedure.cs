using System;
using System.Threading;
using System.Threading.Tasks;
using EasyFramework.UI;

namespace EasyFramework
{
    public sealed class LoginProcedure : ProcedureBase
    {
        private EasyUIView _view;

        protected override async Task<Type> OnEnterAsync(CancellationToken cancellationToken)
        {
            string location = Context.Preset.LoginViewLocation;
            if (!string.IsNullOrWhiteSpace(location))
                _view = await EasyUIManager.Instance.OpenAsync(location);
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        protected override Task OnExitAsync(CancellationToken cancellationToken)
        {
            if (_view != null) EasyUIManager.Instance?.Close(_view);
            _view = null;
            return Task.CompletedTask;
        }
    }
}
