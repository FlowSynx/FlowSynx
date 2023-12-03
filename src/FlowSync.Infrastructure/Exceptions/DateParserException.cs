﻿using FlowSync.Abstractions.Exceptions;

namespace FlowSync.Infrastructure.Exceptions;

public class DateParserException : FlowSyncBaseException
{
    public DateParserException(string message) : base(message) { }
    public DateParserException(string message, Exception inner) : base(message, inner) { }
}