namespace Atlas.Api.Services;
public class ApiException(int status, string message) : Exception(message) { public int Status { get; } = status; }
