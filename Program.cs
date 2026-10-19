using System.Diagnostics;
using Serilog;
using Scalar.AspNetCore;
using System.Net.Mail;
using System.Collections.Concurrent;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/api-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

var app = builder.Build();
app.UseMiddleware<AuthenticationMiddleware>();
app.UseMiddleware<ErrorHandlingMiddleware>();
// app.UseMiddleware<RequestResponseLoggingMiddleware>();
app.UseSerilogRequestLogging();

var routeCounters = new ConcurrentDictionary<string, int>();

app.Use(async (context, next) =>
{
  await next();

  var endpoint = context.GetEndpoint();

  var routeName =
      endpoint?.Metadata.GetMetadata<RouteNameMetadata>()?.RouteName
      ?? endpoint?.DisplayName
      ?? "Unknown";

  routeCounters.AddOrUpdate(
      routeName,
      1,
      (_, count) => count + 1);
});

if (app.Environment.IsDevelopment())
{
  app.MapOpenApi();
  app.MapScalarApiReference();
}

var users = new List<User>
{
    new()
    {
        Id = 1,
        FirstName = "John",
        LastName = "Doe",
        Email = "john.doe@example.com"
    },
    new()
    {
        Id = 2,
        FirstName = "Jane",
        LastName = "Smith",
        Email = "jane.smith@example.com"
    }
};


var nextId = users.Max(u => u.Id) + 1;

bool IsValidUser(User user)
{
  if (string.IsNullOrWhiteSpace(user.FirstName))
    return false;

  if (string.IsNullOrWhiteSpace(user.LastName))
    return false;

  if (string.IsNullOrWhiteSpace(user.Email))
    return false;

  try
  {
    _ = new MailAddress(user.Email);
    return true;
  }
  catch
  {
    return false;
  }
}

app.UseExceptionHandler(exceptionApp =>
{
  exceptionApp.Run(async context =>
  {
    context.Response.StatusCode = 500;

    await context.Response.WriteAsJsonAsync(new
    {
      Error = "An unexpected error occurred."
    });
  });
});

app.MapGet("/error", () =>
{
  throw new Exception("Something went wrong!");
});

app.MapGet("/api/stats", () =>
{
  return Results.Ok(routeCounters);
})
.WithName("GetApiStats")
.WithSummary("Returns API usage statistics");

// GET ALL USERS
app.MapGet("/api/users", () =>
{
  return Results.Ok(users);
})
.WithName("GetUsers")
.WithSummary("Get all users");


// GET USER BY ID
app.MapGet("/api/users/{id:int}", (int id) =>
{
  var user = users.FirstOrDefault(u => u.Id == id);

  return user is null
      ? Results.NotFound($"User with ID {id} was not found.")
      : Results.Ok(user);
})
.WithName("GetUserById")
.WithSummary("Get a user by ID");


app.MapPost("/api/users", (User user) =>
{
  try
  {
    if (!IsValidUser(user))
    {
      return Results.BadRequest("Invalid user data.");
    }

    if (users.Any(u =>
        u.Email.Equals(user.Email,
            StringComparison.OrdinalIgnoreCase)))
    {
      return Results.BadRequest("Email already exists.");
    }

    user.Id = nextId++;

    users.Add(user);

    return Results.Created(
        $"/api/users/{user.Id}",
        user);
  }
  catch (Exception ex)
  {
    return Results.Problem(
        $"An unexpected error occurred: {ex.Message}");
  }
}).WithName("CreateUser")
  .WithSummary("Create a new user");


// UPDATE USER
app.MapPut("/api/users/{id:int:min(1)}",
    (int id, User updatedUser) =>
{
  try
  {
    if (!IsValidUser(updatedUser))
    {
      return Results.BadRequest("Invalid user data.");
    }

    var existingUser =
        users.FirstOrDefault(u => u.Id == id);

    if (existingUser is null)
    {
      return Results.NotFound(
          $"User with ID {id} was not found.");
    }

    var emailExists = users.Any(u =>
        u.Id != id &&
        u.Email.Equals(updatedUser.Email,
            StringComparison.OrdinalIgnoreCase));

    if (emailExists)
    {
      return Results.BadRequest(
          "Email already exists.");
    }

    existingUser.FirstName = updatedUser.FirstName;
    existingUser.LastName = updatedUser.LastName;
    existingUser.Email = updatedUser.Email;

    return Results.Ok(existingUser);
  }
  catch (Exception ex)
  {
    return Results.Problem(
        $"An unexpected error occurred: {ex.Message}");
  }
})
.WithName("UpdateUser")
.WithSummary("Update an existing user");


// DELETE USER
app.MapDelete("/api/users/{id:int:min(1)}", (int id) =>
{
  try
  {
    var user = users.FirstOrDefault(u => u.Id == id);

    if (user is null)
    {
      return Results.NotFound(
          $"User with ID {id} was not found.");
    }

    users.Remove(user);

    return Results.NoContent();
  }
  catch (Exception ex)
  {
    return Results.Problem(
        $"An unexpected error occurred: {ex.Message}");
  }
})
.WithName("DeleteUser")
.WithSummary("Delete a user");

app.Run();


// MODEL
public class User
{
  public int Id { get; set; }

  public string FirstName { get; set; } = string.Empty;

  public string LastName { get; set; } = string.Empty;

  public string Email { get; set; } = string.Empty;
}


public class RequestResponseLoggingMiddleware
{
  private readonly RequestDelegate _next;

  public RequestResponseLoggingMiddleware(RequestDelegate next)
  {
    _next = next;
  }

  public async Task InvokeAsync(HttpContext context)
  {
    var stopwatch = Stopwatch.StartNew();

    try
    {
      Log.Information(
          "Incoming Request: {Method} {Path}",
          context.Request.Method,
          context.Request.Path);

      await _next(context);

      stopwatch.Stop();

      Log.Information(
          "Outgoing Response: {StatusCode} completed in {ElapsedMilliseconds}ms",
          context.Response.StatusCode,
          stopwatch.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
      stopwatch.Stop();

      Log.Error(
          ex,
          "Unhandled exception for {Method} {Path} after {ElapsedMilliseconds}ms",
          context.Request.Method,
          context.Request.Path,
          stopwatch.ElapsedMilliseconds);

      throw;
    }
  }
}

public class ErrorHandlingMiddleware
{
  private readonly RequestDelegate _next;

  public ErrorHandlingMiddleware(RequestDelegate next)
  {
    _next = next;
  }

  public async Task InvokeAsync(HttpContext context)
  {
    try
    {
      await _next(context);
    }
    catch (Exception ex)
    {
      Log.Error(
          ex,
          "Unhandled exception while processing {Method} {Path}",
          context.Request.Method,
          context.Request.Path);

      context.Response.StatusCode = StatusCodes.Status500InternalServerError;
      context.Response.ContentType = "application/json";

      var response = new
      {
        Title = "Internal Server Error",
        Status = 500,
        Detail = "An unexpected error occurred."
      };

      await context.Response.WriteAsync(
          JsonSerializer.Serialize(response));
    }
  }
}

public class AuthenticationMiddleware
{
  private readonly RequestDelegate _next;

  private const string ValidToken = "1234";

  public AuthenticationMiddleware(RequestDelegate next)
  {
    _next = next;
  }

  public async Task InvokeAsync(HttpContext context)
  {
    if (!context.Request.Headers.TryGetValue(
            "x-token-security",
            out var token))
    {
      context.Response.StatusCode = StatusCodes.Status401Unauthorized;

      await context.Response.WriteAsJsonAsync(new
      {
        Message = "Authentication token is missing."
      });

      return;
    }

    if (token != ValidToken)
    {
      context.Response.StatusCode = StatusCodes.Status401Unauthorized;

      await context.Response.WriteAsJsonAsync(new
      {
        Message = "Invalid authentication token."
      });

      return;
    }

    await _next(context);
  }
}