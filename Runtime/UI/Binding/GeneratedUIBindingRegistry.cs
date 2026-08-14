using System;
using System.Collections.Generic;

namespace EasyFramework.UI
{
    public static class GeneratedUIBindingRegistry
    {
        private static readonly Dictionary<string, Action<UIBindingContext>> Installers =
            new Dictionary<string, Action<UIBindingContext>>(StringComparer.Ordinal);

        public static void Register(string bindingId, Action<UIBindingContext> installer)
        {
            if (string.IsNullOrWhiteSpace(bindingId)) throw new ArgumentException("Binding id is required.", nameof(bindingId));
            if (installer == null) throw new ArgumentNullException(nameof(installer));
            Installers[bindingId] = installer;
        }

        internal static bool TryInstall(string bindingId, UIBindingContext context)
        {
            if (string.IsNullOrEmpty(bindingId)) return false;
            if (!Installers.TryGetValue(bindingId, out var installer)) return false;
            installer(context);
            return true;
        }
    }
}
