// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Windows.Run.SDK;

namespace AzureCommandPaletteExtension;

[ComVisible(true)]
[Guid("3A82287D-75C3-4C73-8E66-C21D9FDE679C")]
[ComDefaultInterface(typeof(IExtension))]
public sealed class SampleExtension : IExtension
{
    private readonly ManualResetEvent _extensionDisposedEvent;

    public SampleExtension(ManualResetEvent extensionDisposedEvent)
    {
        this._extensionDisposedEvent = extensionDisposedEvent;
    }

    public object GetProvider(ProviderType providerType)
    {
        switch (providerType)
        {
            case ProviderType.Actions:
                return new CommandPaletteActionsProvider();
            default:
#pragma warning disable CS8603 // Possible null reference return.
                return null;
#pragma warning restore CS8603 // Possible null reference return.
        }
    }

    public void Dispose()
    {
        this._extensionDisposedEvent.Set();
    }
}
