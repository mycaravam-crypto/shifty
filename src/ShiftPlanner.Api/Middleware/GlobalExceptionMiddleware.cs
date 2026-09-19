using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ShiftPlanner.Api.Middleware;

// Backstop for every exception that escapes a controller action. Before this existed, an
// unhandled exception (e.g. deleting an Employee who still has ShiftAssignments — a Restrict FK,
// see ApplicationDbContext — throwing a raw DbUpdateException) produced Kestrel's default 500
// with an *empty body*, so a manager saw "delete failed" with literally no reason anywhere
// (not in the response, not surfaced by the frontend). Controllers that can check a precondition
// up front (uniqueness, existence, lifecycle state, referencing rows) still should — that gives a
// far more specific message than this can — but this guarantees every response, even one from a
// path nobody thought to guard, at least carries a readable, actionable message instead of
// silence. Registered first in the pipeline (Program.cs) so it wraps everything downstream.
public class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation
        })
        {
            logger.LogWarning(ex, "Blocked a write at {Method} {Path} due to a foreign-key constraint.",
                context.Request.Method, context.Request.Path);
            await WriteErrorAsync(context, StatusCodes.Status409Conflict,
                "Der Vorgang wurde abgebrochen: Dieser Datensatz wird noch von anderen Einträgen " +
                "verwendet. Bitte entfernen Sie zuerst die verknüpften Einträge und versuchen Sie " +
                "es erneut.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        })
        {
            logger.LogWarning(ex, "Blocked a write at {Method} {Path} due to a unique constraint.",
                context.Request.Method, context.Request.Path);
            await WriteErrorAsync(context, StatusCodes.Status409Conflict,
                "Dieser Eintrag existiert bereits und kann nicht doppelt angelegt werden.");
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Unexpected database error at {Method} {Path}.",
                context.Request.Method, context.Request.Path);
            await WriteErrorAsync(context, StatusCodes.Status500InternalServerError,
                "Der Datenbankvorgang ist fehlgeschlagen. Bitte versuchen Sie es später erneut.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception at {Method} {Path}.",
                context.Request.Method, context.Request.Path);
            await WriteErrorAsync(context, StatusCodes.Status500InternalServerError,
                "Ein unerwarteter Fehler ist aufgetreten. Bitte versuchen Sie es erneut oder " +
                "wenden Sie sich an den Support, falls das Problem bestehen bleibt.");
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string message)
    {
        // Can't do anything useful if the response body has already started streaming to the
        // client (e.g. the exception happened mid-write) — rethrowing would just crash the
        // request a second time, so let it die quietly; the exception above is already logged.
        if (context.Response.HasStarted)
            return;

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        // A plain JSON string, matching every controller's own `BadRequest("...")`/
        // `Conflict("...")` responses elsewhere in this codebase — so the frontend's existing
        // `e.response.data` string-based error handling picks this up with no special-casing.
        await context.Response.WriteAsJsonAsync(message);
    }
}
