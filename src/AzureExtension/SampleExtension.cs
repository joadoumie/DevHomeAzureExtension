// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;
using AzureCommandPaletteExtension;
using Microsoft.Windows.Run.SDK;

namespace SampleDevPalExtension;

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
                // ignore the possible null reference warning
#pragma warning disable CS8603
                return null;
#pragma warning restore CS8603
        }
    }

    public void Dispose()
    {
        this._extensionDisposedEvent.Set();
    }
}
