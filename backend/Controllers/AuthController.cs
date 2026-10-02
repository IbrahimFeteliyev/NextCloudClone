using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Atlas.Api.Auth;
using Atlas.Api.Data;
using Atlas.Api.DTOs;
using Atlas.Api.Entities;
using Atlas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
namespace Atlas.Api.Controllers;
[ApiController, Route("api/auth")]
public class AuthController(AppDbContext db, IConfiguration config, PasswordHasher<User> hasher, CurrentUser current, AuditService audit) : ControllerBase
{
    [HttpPost("login")]
    public async Task<object> Login(LoginRequest request)
    {
        var user = await db.Users.Include(x => x.Role).SingleOrDefaultAsync(x => x.Email == request.Email.Trim().ToLowerInvariant());
        if (user == null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed) throw new ApiException(401, "The email or password is incorrect.");
        var expires = DateTime.UtcNow.AddMinutes(config.GetValue("Jwt:LifetimeMinutes", 480));
        var token = new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"],
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Name), new Claim(ClaimTypes.Role, user.Role.Name)],
            expires: expires, signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256));
        audit.Add(user.Id, "LOGIN", null, "user", $"{user.Email} signed in"); await db.SaveChangesAsync();
        return new { token = new JwtSecurityTokenHandler().WriteToken(token), expiresAt = expires, user = new UserDto(user.Id, user.Email, user.Name, user.Role.Name) };
    }
    [Authorize, HttpGet("me")]
    public async Task<UserDto> Me() { var u = await db.Users.Include(x => x.Role).SingleAsync(x => x.Id == current.Id); return new(u.Id, u.Email, u.Name, u.Role.Name); }
    [Authorize, HttpGet("users")]
    public Task<List<UserDto>> Users() => db.Users.Include(x => x.Role).OrderBy(x => x.Name).Select(x => new UserDto(x.Id, x.Email, x.Name, x.Role.Name)).ToListAsync();
}
