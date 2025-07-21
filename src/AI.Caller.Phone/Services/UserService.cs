using AI.Caller.Phone.Entities;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AI.Caller.Phone.Services;
public class UserService {
    private readonly AppDbContext _context;

    public UserService(AppDbContext context) {
        _context = context;
    }

    public async Task<User> RegisterAsync(string username, string password) {
        if (await _context.Users.AnyAsync(u => u.Username == username)) {
            throw new InvalidOperationException("Username already exists.");
        }

        var user = new User {
            Username = username,
            Password = password
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return user;
    }
}