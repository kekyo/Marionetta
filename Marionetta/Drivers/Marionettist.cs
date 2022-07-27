﻿/////////////////////////////////////////////////////////////////////////////////////
//
// Marionetta - Split dirty component into sandboxed outprocess.
// Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)
//
// Licensed under Apache-v2: https://opensource.org/licenses/Apache-2.0
//
/////////////////////////////////////////////////////////////////////////////////////

using Marionetta.Messengers;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace Marionetta.Drivers;

public sealed class Marionettist : Driver<ActiveMessenger>
{
    private readonly AnonymousPipeServerStream serverOutStream;
    private readonly AnonymousPipeServerStream serverInStream;
    private Process puppetProcess = new();
    private bool exited;

    public Marionettist(
        string puppetPath,
        string workingDirectoryPath,
        string[] additionalArgs)
    {
        this.serverOutStream = new AnonymousPipeServerStream(
            PipeDirection.Out, HandleInheritability.Inheritable);
        this.serverInStream = new AnonymousPipeServerStream(
            PipeDirection.In, HandleInheritability.Inheritable);

        this.messenger = new ActiveMessenger(
            this.serverInStream, this.serverOutStream);

        this.puppetProcess.StartInfo.FileName = puppetPath;
        this.puppetProcess.StartInfo.Arguments =
            $"{this.serverOutStream.GetClientHandleAsString()} {this.serverInStream.GetClientHandleAsString()} " +
            string.Join(" ", additionalArgs);
        this.puppetProcess.StartInfo.UseShellExecute = false;
        this.puppetProcess.StartInfo.CreateNoWindow = true;
#if !NETSTANDARD1_3 && !NETSTANDARD1_4 && !NETSTANDARD1_5 && !NETSTANDARD1_6
        this.puppetProcess.StartInfo.ErrorDialog = false;
#endif
        this.puppetProcess.StartInfo.WorkingDirectory = workingDirectoryPath;

        this.puppetProcess.Exited += (s, e) => this.exited = true;
        this.puppetProcess.EnableRaisingEvents = true;
    }

    public override void Dispose()
    {
        if (this.puppetProcess is { } puppetProcess)
        {
            this.puppetProcess = null!;

            base.Dispose();

            this.serverOutStream.Dispose();
            this.serverInStream.Dispose();

            var watcher = new Thread(() =>
            {
                for (var index = 0; index < 10; index++)
                {
                    if (this.exited)
                    {
                        return;
                    }

                    Thread.Sleep(TimeSpan.FromMilliseconds(1000));
                }

                puppetProcess.Kill();
                puppetProcess.Dispose();
            });
            watcher.IsBackground = false;   // Force alives for watcher in process terminating.
            watcher.Start();
        }
    }

    public void Start()
    {
        this.messenger.Start();
        this.puppetProcess.Start();
    }
}