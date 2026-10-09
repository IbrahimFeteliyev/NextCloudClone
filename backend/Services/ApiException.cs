namespace Atlas.Api.Services;
public class ApiException(int status, string message, object? details = null) : Exception(message) { public int Status { get; } = status; public object? Details { get; } = details; }
