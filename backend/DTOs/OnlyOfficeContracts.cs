namespace Atlas.Api.DTOs;
public record OfficeConfigDto(string DocumentServerUrl, string Mode, int Version, object Config);
