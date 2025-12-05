using MaiAmTinhThuong.Data;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace MaiAmTinhThuong.Components
{
    public class MaiAmMenuViewComponent : ViewComponent
    {
        private readonly ApplicationDbContext _context;

        public MaiAmMenuViewComponent(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            try
            {
                // Kiểm tra database có thể kết nối được không
                if (_context.Database.CanConnect())
                {
                    var maiAms = await _context.MaiAms.OrderBy(m => m.Id).ToListAsync();
                    return View(maiAms);
                }
                else
                {
                    // Trả về empty list nếu không kết nối được
                    return View(new List<Models.MaiAm>());
                }
            }
            catch
            {
                // Trả về empty list nếu có lỗi
                return View(new List<Models.MaiAm>());
            }
        }
    }
}
