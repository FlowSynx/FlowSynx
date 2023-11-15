﻿using System.ComponentModel.DataAnnotations;
using System.Net;
using FlowSync.Core.Serialization;
using FlowSync.Models;

namespace FlowSync.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly ISerializer _serializer;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, ISerializer serializer)
    {
        this._next = next;
        _logger = logger;
        _serializer = serializer;
    }

    public async Task Invoke(HttpContext context /* other dependencies */)
    {
        try
        {
            await _next(context);
        }
        catch (BadHttpRequestException ex)
        {
            await HandleExceptionAsync(context, ex);
        }
        catch (Exception exceptionObj)
        {
            await HandleExceptionAsync(context, exceptionObj);
        }
    }

    private Task HandleExceptionAsync(HttpContext context, BadHttpRequestException exception)
    {
        string? result = null;
        context.Response.ContentType = _serializer.ContentMineType;
        if (exception is BadHttpRequestException)
        {
            result = new ErrorDetails(_serializer, exception.Message, (int)exception.StatusCode).ToString();
            context.Response.StatusCode = (int)exception.StatusCode;
        }
        else
        {
            result = new ErrorDetails(_serializer, "Runtime Error", (int)HttpStatusCode.BadRequest).ToString();
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        }
        _logger.LogError(exception.Message);
        return context.Response.WriteAsync(result);
    }

    private Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        _logger.LogError(exception.Message);
        var result = new ErrorDetails(_serializer, exception.Message, (int)HttpStatusCode.InternalServerError).ToString();
        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        return context.Response.WriteAsync(result);
    }
}