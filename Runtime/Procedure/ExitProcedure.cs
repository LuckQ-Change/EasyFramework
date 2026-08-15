using System;
using System.Threading;
using System.Threading.Tasks;
using EasyFramework.UI;
using UnityEngine;

namespace EasyFramework
{
    public sealed class ExitProcedure : ProcedureBase
    {
        protected override Task<Type> OnEnterAsync(CancellationToken cancellationToken)
        {
            EasyUIManager ui = EasyUIManager.Instance;
            ui?.CloseAll();
            ui?.ReleaseAllPreloaded();
            AssetModule.Instance?.ReleaseAll();
            if (Context.Preset.QuitApplicationOnExit && Application.isPlaying)
                Application.Quit();
            return Stay();
        }
    }
}
