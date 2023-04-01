using AspNetCoreFido2MFA.Data;
using Fido2NetLib;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using StoredCredential = AspNetCoreFido2MFA.Models.StoredCredential;

namespace AspNetCoreFido2MFA.Pages;

public class DashboardModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly AppDbContext _context;
    public Fido2User User { get; set; }

    public List<StoredCredential> ExistingCredentials { get; set; } = new();

    public DashboardModel(ILogger<IndexModel> logger, AppDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    public async Task OnGet(string username)
    {
        User = await _context.Fido2Users.SingleOrDefaultAsync(u => u.Name.Equals(username));
        ExistingCredentials = await _context.StoredCredentials.Where(c => c.UserId.SequenceEqual(User.Id)).ToListAsync();
    }
}