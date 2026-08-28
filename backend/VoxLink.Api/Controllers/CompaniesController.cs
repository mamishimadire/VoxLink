using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxLink.Api.Auth;
using VoxLink.Api.Data;

namespace VoxLink.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/companies")]
public class CompaniesController : ControllerBase
{
    private readonly VoxLinkDbContext _db;

    public CompaniesController(VoxLinkDbContext db)
    {
        _db = db;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMyCompany(CancellationToken cancellationToken)
    {
        var companyId = User.GetCompanyId();
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);
        return company is null ? NotFound() : Ok(company);
    }
}
