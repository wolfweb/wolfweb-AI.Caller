using AI.Caller.Phone.Entities;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AI.Caller.Phone.Services
{
    public class ContactService
    {
        private readonly AppDbContext _context;

        public ContactService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<Contact?> GetContactByIdAsync(int id)
        {
            return await _context.Contacts.FindAsync(id);
        }

        public async Task<List<Contact>> GetContactsByUserIdAsync(int userId)
        {
            return await _context.Contacts
                .Where(c => c.UserId == userId)
                .ToListAsync();
        }

        public async Task AddContactAsync(Contact contact)
        {
            await _context.Contacts.AddAsync(contact);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateContactAsync(Contact contact)
        {
            _context.Contacts.Update(contact);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteContactAsync(int id)
        {
            var contact = await _context.Contacts.FindAsync(id);
            if (contact != null)
            {
                _context.Contacts.Remove(contact);
                await _context.SaveChangesAsync();
            }
        }
    }
}