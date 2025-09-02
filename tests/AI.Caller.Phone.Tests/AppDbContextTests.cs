using AI.Caller.Phone.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AI.Caller.Phone.Tests;

public class AppDbContextTests : IDisposable {
    private readonly AppDbContext _context;
    private readonly string _testDbPath;

    public AppDbContextTests() {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_dbcontext_{Guid.NewGuid()}.db");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_testDbPath}")
            .Options;

        var configuration = new ConfigurationBuilder().Build();
        _context = new AppDbContext(options, configuration);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task SipAccount_CRUD_Operations_ShouldWork() {
        var sipAccount = new SipAccount {
            SipUsername = "test@sip.com",
            SipPassword = "password123",
            SipServer = "sip.example.com",
            IsActive = true
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var savedAccount = await _context.SipAccounts
            .FirstOrDefaultAsync(s => s.SipUsername == "test@sip.com");

        Assert.NotNull(savedAccount);
        Assert.Equal("test@sip.com", savedAccount.SipUsername);
        Assert.Equal("password123", savedAccount.SipPassword);
        Assert.Equal("sip.example.com", savedAccount.SipServer);
        Assert.True(savedAccount.IsActive);
        Assert.True(savedAccount.Id > 0);

        savedAccount.SipServer = "sip.newserver.com";
        savedAccount.IsActive = false;
        await _context.SaveChangesAsync();

        var updatedAccount = await _context.SipAccounts.FindAsync(savedAccount.Id);
        Assert.NotNull(updatedAccount);
        Assert.Equal("sip.newserver.com", updatedAccount.SipServer);
        Assert.False(updatedAccount.IsActive);

        // Delete
        _context.SipAccounts.Remove(updatedAccount);
        await _context.SaveChangesAsync();

        var deletedAccount = await _context.SipAccounts.FindAsync(savedAccount.Id);
        Assert.Null(deletedAccount);
    }

    [Fact]
    public async Task User_SipAccount_Relationship_ShouldWork() {
        var sipAccount = new SipAccount {
            SipUsername = "shared@sip.com",
            SipPassword = "password123",
            SipServer = "sip.example.com"
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var user1 = new User {
            Username = "user1",
            Password = "pass1",
            SipAccountId = sipAccount.Id
        };

        var user2 = new User {
            Username = "user2",
            Password = "pass2",
            SipAccountId = sipAccount.Id
        };

        _context.Users.AddRange(user1, user2);
        await _context.SaveChangesAsync();

        var accountWithUsers = await _context.SipAccounts
            .Include(s => s.Users)
            .FirstOrDefaultAsync(s => s.Id == sipAccount.Id);

        Assert.NotNull(accountWithUsers);
        Assert.Equal(2, accountWithUsers.Users.Count);
        Assert.Contains(accountWithUsers.Users, u => u.Username == "user1");
        Assert.Contains(accountWithUsers.Users, u => u.Username == "user2");

        var userWithAccount = await _context.Users
            .Include(u => u.SipAccount)
            .FirstOrDefaultAsync(u => u.Username == "user1");

        Assert.NotNull(userWithAccount);
        Assert.NotNull(userWithAccount.SipAccount);
        Assert.Equal("shared@sip.com", userWithAccount.SipAccount.SipUsername);
    }

    [Fact]
    public async Task User_Contact_Relationship_ShouldWork() {
        // Arrange
        var user = new User {
            Username = "contactuser",
            Password = "pass"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var contact1 = new Contact {
            Name = "Contact 1",
            PhoneNumber = "1234567890",
            UserId = user.Id
        };

        var contact2 = new Contact {
            Name = "Contact 2",
            PhoneNumber = "0987654321",
            UserId = user.Id
        };

        // Act
        _context.Contacts.AddRange(contact1, contact2);
        await _context.SaveChangesAsync();

        // Assert
        var userWithContacts = await _context.Users
            .Include(u => u.Contacts)
            .FirstOrDefaultAsync(u => u.Id == user.Id);

        Assert.NotNull(userWithContacts);
        Assert.NotNull(userWithContacts.Contacts);
        Assert.Equal(2, userWithContacts.Contacts.Count);
        Assert.Contains(userWithContacts.Contacts, c => c.Name == "Contact 1");
        Assert.Contains(userWithContacts.Contacts, c => c.Name == "Contact 2");
    }

    [Fact]
    public async Task SipAccount_UniqueConstraint_ShouldBeEnforced() {
        // Arrange
        var sipAccount1 = new SipAccount {
            SipUsername = "unique@sip.com",
            SipPassword = "password1",
            SipServer = "sip.server1.com"
        };

        var sipAccount2 = new SipAccount {
            SipUsername = "unique@sip.com", // 相同的SipUsername
            SipPassword = "password2",
            SipServer = "sip.server2.com"
        };

        _context.SipAccounts.Add(sipAccount1);
        await _context.SaveChangesAsync();

        _context.SipAccounts.Add(sipAccount2);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(async () => {
            await _context.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task User_SipAccount_CascadeDelete_ShouldSetNull() {
        var sipAccount = new SipAccount {
            SipUsername = "cascade@sip.com",
            SipPassword = "password",
            SipServer = "sip.server.com"
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var user = new User {
            Username = "cascadeuser",
            Password = "pass",
            SipAccountId = sipAccount.Id
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _context.SipAccounts.Remove(sipAccount);
        await _context.SaveChangesAsync();

        var updatedUser = await _context.Users.FindAsync(user.Id);
        Assert.NotNull(updatedUser);
        Assert.Null(updatedUser.SipAccountId);
    }

    [Fact]
    public async Task User_Contact_CascadeDelete_ShouldDeleteContacts() {
        // Arrange
        var user = new User {
            Username = "deleteuser",
            Password = "pass"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var contact = new Contact {
            Name = "Test Contact",
            PhoneNumber = "1234567890",
            UserId = user.Id
        };

        _context.Contacts.Add(contact);
        await _context.SaveChangesAsync();

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        var remainingContact = await _context.Contacts.FindAsync(contact.Id);
        Assert.Null(remainingContact);
    }

    [Fact]
    public async Task Database_Indexes_ShouldExist() {
        var indexQuery = @"
            SELECT name FROM sqlite_master 
            WHERE type='index' AND name LIKE 'IX_%'
            ORDER BY name";

        var connection = _context.Database.GetDbConnection();
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = indexQuery;

        var indexes = new List<string>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            indexes.Add(reader.GetString(0));
        }

        Assert.Contains("IX_SipAccounts_SipUsername", indexes);
        Assert.Contains("IX_SipAccounts_IsActive", indexes);
        Assert.Contains("IX_Users_Username", indexes);
        Assert.Contains("IX_Users_SipUsername", indexes);
        Assert.Contains("IX_Users_SipAccountId", indexes);
        Assert.Contains("IX_Contacts_UserId", indexes);
        Assert.Contains("IX_Contacts_PhoneNumber", indexes);
    }

    [Fact]
    public async Task ComplexQuery_WithMultipleJoins_ShouldWork() {
        var sipAccount = new SipAccount {
            SipUsername = "complex@sip.com",
            SipPassword = "password",
            SipServer = "sip.server.com"
        };

        _context.SipAccounts.Add(sipAccount);
        await _context.SaveChangesAsync();

        var user = new User {
            Username = "complexuser",
            Password = "pass",
            SipAccountId = sipAccount.Id,
            SipAccount = sipAccount,
            SipRegistered = true
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var contact = new Contact {
            Name = "Complex Contact",
            PhoneNumber = "1234567890",
            UserId = user.Id
        };

        _context.Contacts.Add(contact);
        await _context.SaveChangesAsync();

        var result = await _context.Users
            .Include(u => u.SipAccount)
            .Include(u => u.Contacts)
            .Where(u => u.SipRegistered && u.SipAccount != null && u.SipAccount.IsActive)
            .FirstOrDefaultAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal("complexuser", result.Username);
        Assert.NotNull(result.SipAccount);
        Assert.Equal("complex@sip.com", result.SipAccount.SipUsername);
        Assert.NotNull(result.Contacts);
        Assert.Single(result.Contacts);
        Assert.Equal("Complex Contact", result.Contacts.First().Name);
    }

    public void Dispose() {
        _context.Dispose();
        if (File.Exists(_testDbPath)) {
            File.Delete(_testDbPath);
        }
    }
}