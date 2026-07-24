// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;

namespace PrimalEditor.Utilities;

enum MessageType
{
    Info = 0x01,
    Warning = 0x02,
    Error = 0x04,
}

class LogMessage(MessageType type, string msg, string file, string caller, int line)
{
    public DateTime Time { get; } = DateTime.Now;
    public MessageType MessageType { get; } = type;
    public string Message { get; } = msg;
    public string File { get; } = Path.GetFileName(file);
    public string Caller { get; } = caller;
    public int Line { get; } = line;
    public string MetaData => $"{File}: {Caller} ({Line})";
}

static class Logger
{
    private static int _messageFilter = (int)(MessageType.Info | MessageType.Warning | MessageType.Error);
    private static readonly ObservableCollection<LogMessage> _messages = [];
    public static ReadOnlyObservableCollection<LogMessage> Messages
    { get; } = new ReadOnlyObservableCollection<LogMessage>(_messages);
    public static CollectionViewSource FilteredMessages
    { get; } = new CollectionViewSource() { Source = Messages };

    public static async void Log(MessageType type, string msg,
        [CallerFilePath] string file = "", [CallerMemberName] string caller = "",
        [CallerLineNumber] int line = 0)
    {
        await Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            _messages.Add(new LogMessage(type, msg, file, caller, line));
        }));
    }

    public static async void Clear()
    {
        await Application.Current.Dispatcher.BeginInvoke(new Action(_messages.Clear));
    }

    public static void SetMessageFilter(int mask)
    {
        _messageFilter = mask;
        FilteredMessages.View.Refresh();
    }

    static Logger()
    {
        FilteredMessages.Filter += (s, e) =>
        {
            var type = (int)(e.Item as LogMessage).MessageType;
            e.Accepted = (type & _messageFilter) != 0;
        };
    }
}
