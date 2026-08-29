using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxLink.Api.Auth;
using VoxLink.Api.Data;
using VoxLink.Api.Models;

namespace VoxLink.Api.Controllers;

public record ContactResponse(Guid Id, string? FirstName, string? LastName, string PhoneNumber, string? Email, string? Notes, bool IsFavorite);
public record CreateContactRequest(string? FirstName, string? LastName, string PhoneNumber, string? Email, string? Notes);
public record SetFavoriteRequest(bool IsFavorite);

[ApiController]
[Authorize]
[Route("api/contacts")]
public class ContactsController : ControllerBase
{
    private readonly VoxLinkDbContext _db;

    public ContactsController(VoxLinkDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetContacts(CancellationToken cancellationToken)
    {
        var contacts = await _db.Contacts
            .OrderByDescending(c => c.IsFavorite).ThenBy(c => c.FirstName).ThenBy(c => c.LastName)
            .Select(c => new ContactResponse(c.Id, c.FirstName, c.LastName, c.PhoneNumber, c.Email, c.Notes, c.IsFavorite))
            .ToListAsync(cancellationToken);

        return Ok(contacts);
    }

    [HttpPut("{id:guid}/favorite")]
    public async Task<IActionResult> SetFavorite(Guid id, SetFavoriteRequest request, CancellationToken cancellationToken)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null) return NotFound();

        contact.IsFavorite = request.IsFavorite;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost]
    public async Task<IActionResult> CreateContact(CreateContactRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return BadRequest(new { message = "A phone number is required." });
        }

        var contact = new Contact
        {
            Id = Guid.NewGuid(),
            CompanyId = User.GetCompanyId(),
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            Email = request.Email,
            Notes = request.Notes,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new ContactResponse(contact.Id, contact.FirstName, contact.LastName, contact.PhoneNumber, contact.Email, contact.Notes, contact.IsFavorite));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteContact(Guid id, CancellationToken cancellationToken)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null) return NotFound();

        _db.Contacts.Remove(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
