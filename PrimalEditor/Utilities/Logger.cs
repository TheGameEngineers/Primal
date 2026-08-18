// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;

namespace PrimalEditor.Utilities;

[Flags]
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
    private static MessageType _messageFilter = MessageType.Info | MessageType.Warning | MessageType.Error;
    private static readonly ObservableCollection<LogMessage> _messages = [];
    public static ReadOnlyObservableCollection<LogMessage> Messages
    { get; } = new ReadOnlyObservableCollection<LogMessage>(_messages);
    public static CollectionViewSource FilteredMessages
    { get; } = new CollectionViewSource() { Source = Messages };

    public static void Log(MessageType type, string msg,
        [CallerFilePath] string file = "", [CallerMemberName] string caller = "",
        [CallerLineNumber] int line = 0)
    {
        Application.Current.Dispatcher?.BeginInvoke(() => _messages.Add(new(type, msg, file, caller, line)));
    }

    public static void Clear()
    {
        Application.Current.Dispatcher?.BeginInvoke(_messages.Clear);
    }

    public static void SetMessageFilter(MessageType mask)
    {
        _messageFilter = mask;
        FilteredMessages.View.Refresh();
    }

    static Logger()
    {
        FilteredMessages.Filter += (s, e) =>
        {
            var type = (e.Item as LogMessage)?.MessageType;
            e.Accepted = type != null && (type & _messageFilter) != 0;
        };
    }
}
