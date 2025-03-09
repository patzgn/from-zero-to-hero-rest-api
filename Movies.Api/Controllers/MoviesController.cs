using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Movies.Api.Auth;
using Movies.Api.Mapping;
using Movies.Application.Services;
using Movies.Contracts.Requests;
using Movies.Contracts.Responses;

namespace Movies.Api.Controllers;

[ApiController]
[ApiVersion(1.0)]
public class MoviesController : ControllerBase
{
    private readonly IMovieService _movieService;
    private readonly IOutputCacheStore _outputCache;
    
    public MoviesController(IMovieService movieService, IOutputCacheStore outputCache)
    {
        _movieService = movieService;
        _outputCache = outputCache;
    }

    // [Authorize(AuthConstants.TrustedMemberPolicyName)]
    [ServiceFilter(typeof(ApiKeyAuthFilter))]
    [HttpPost(ApiEndpoints.Movies.Create)]
    [ProducesResponseType(typeof(MovieResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationFailureResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateMovieRequest request, CancellationToken token)
    {
        var movie = request.MapToMovie();
        await _movieService.CreateAsync(movie, token);
        await _outputCache.EvictByTagAsync("movies", token);

        var response = movie.MapToResponse();
        return CreatedAtAction(nameof(Get), new { idOrSlug = movie.Id }, response);
    }

    [MapToApiVersion(1.0)]
    [OutputCache(PolicyName = "MovieCache")]
    // [ResponseCache(Duration = 30, VaryByHeader = "Accept, Accept-Encoding", Location = ResponseCacheLocation.Any)]
    [HttpGet(ApiEndpoints.Movies.Get)]
    [ProducesResponseType(typeof(MovieResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] string idOrSlug, CancellationToken token)
    {
        var userId = HttpContext.GetUserId();

        var movie = Guid.TryParse(idOrSlug, out var id)
            ? await _movieService.GetByIdAsync(id, userId, token)
            : await _movieService.GetBySlugAsync(idOrSlug, userId, token);
        if (movie is null)
        {
            return NotFound();
        }

        var response = movie.MapToResponse();
        return Ok(response);
    }

    [HttpGet(ApiEndpoints.Movies.GetAll)]
    [OutputCache(PolicyName = "MovieCache")]
    // [ResponseCache(Duration = 30, VaryByQueryKeys = new[] { "title", "year", "sortBy", "page", "pageSize" },
        // VaryByHeader = "Accept, Accept-Encoding", Location = ResponseCacheLocation.Any)]
    [ProducesResponseType(typeof(MoviesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] GetAllMoviesRequest request, CancellationToken token)
    {
        var userId = HttpContext.GetUserId();
        var options = request.MapToOptions()
            .WithUserId(userId);

        var movieCount = await _movieService.GetCountAsync(options.Title, options.YearOfRelease, token);
        var movies = await _movieService.GetAllAsync(options, token);

        var response = movies.MapToResponse(request.Page, request.PageSize, movieCount);
        return Ok(response);
    }

    [Authorize(AuthConstants.TrustedMemberPolicyName)]
    [ProducesResponseType(typeof(MovieResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationFailureResponse), StatusCodes.Status400BadRequest)]
    [HttpPut(ApiEndpoints.Movies.Update)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateMovieRequest request,
        CancellationToken token)
    {
        var userId = HttpContext.GetUserId();

        var movie = request.MapToMovie(id);
        var updatedMovie = await _movieService.UpdateAsync(movie, userId, token);
        if (updatedMovie is null)
        {
            return NotFound();
        }

        await _outputCache.EvictByTagAsync("movies", token);
        
        var response = updatedMovie.MapToResponse();
        return Ok(response);
    }

    [Authorize(AuthConstants.AdminUserPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpDelete(ApiEndpoints.Movies.Delete)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken token)
    {
        var deleted = await _movieService.DeleteByIdAsync(id, token);
        if (!deleted)
        {
            return NotFound();
        }

        await _outputCache.EvictByTagAsync("movies", token);

        return Ok();
    }
}
