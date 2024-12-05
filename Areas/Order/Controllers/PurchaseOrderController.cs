﻿using FastReport.Data;
using FastReport.Export.PdfSimple;
using FastReport.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PurchasingSystemProduction.Areas.MasterData.Repositories;
using PurchasingSystemProduction.Areas.Order.Models;
using PurchasingSystemProduction.Areas.Order.Repositories;
using PurchasingSystemProduction.Areas.Order.ViewModels;
using PurchasingSystemProduction.Areas.Warehouse.Repositories;
using PurchasingSystemProduction.Data;
using PurchasingSystemProduction.Models;
using PurchasingSystemProduction.Repositories;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IHostingEnvironment = Microsoft.AspNetCore.Hosting.IHostingEnvironment;

namespace PurchasingSystemProduction.Areas.Order.Controllers
{
    [Area("Order")]
    [Route("Order/[Controller]/[Action]")]
    public class PurchaseOrderController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ApplicationDbContext _applicationDbContext;
        private readonly IPurchaseRequestRepository _purchaseRequestRepository;
        private readonly IUserActiveRepository _userActiveRepository;
        private readonly IProductRepository _productRepository;
        private readonly ITermOfPaymentRepository _termOfPaymentRepository;
        private readonly IApprovalRepository _approvalRepository;
        private readonly IPurchaseOrderRepository _purchaseOrderRepository;
        private readonly IQtyDifferenceRepository _qtyDifferenceRepository;

        private readonly IDataProtector _protector;
        private readonly UrlMappingService _urlMappingService;
        private readonly IHostingEnvironment _hostingEnvironment;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IConfiguration _configuration;


        public PurchaseOrderController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ApplicationDbContext applicationDbContext,
            IPurchaseRequestRepository purchaseRequestRepository,
            IUserActiveRepository userActiveRepository,
            IProductRepository productRepository,
            ITermOfPaymentRepository termOfPaymentRepository,
            IApprovalRepository approvalRepository,
            IPurchaseOrderRepository purchaseOrderRepository,
            IQtyDifferenceRepository qtyDifferenceRepository,

            IDataProtectionProvider provider,
            UrlMappingService urlMappingService,
            IHostingEnvironment hostingEnvironment,
            IWebHostEnvironment webHostEnvironment,
            IConfiguration configuration
        )
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _applicationDbContext = applicationDbContext;
            _purchaseRequestRepository = purchaseRequestRepository;
            _userActiveRepository = userActiveRepository;
            _productRepository = productRepository;
            _termOfPaymentRepository = termOfPaymentRepository;
            _approvalRepository = approvalRepository;
            _purchaseOrderRepository = purchaseOrderRepository;
            _qtyDifferenceRepository = qtyDifferenceRepository;

            _protector = provider.CreateProtector("UrlProtector");
            _urlMappingService = urlMappingService;
            _hostingEnvironment = hostingEnvironment;
            _webHostEnvironment = webHostEnvironment;
            _configuration = configuration;
        }

        public JsonResult LoadProduk(Guid Id)
        {
            var produk = _applicationDbContext.Products.Include(p => p.Supplier).Include(s => s.Measurement).Include(d => d.Discount).Where(p => p.ProductId == Id).FirstOrDefault();
            return new JsonResult(produk);
        }

        public JsonResult LoadPurchaseOrder(Guid Id)
        {
            var podetail = _applicationDbContext.PurchaseOrders
                .Include(p => p.PurchaseOrderDetails)
                .Where(p => p.PurchaseOrderId == Id).FirstOrDefault();
            return new JsonResult(podetail);
        }

        public JsonResult LoadPurchaseOrderDetail(Guid Id)
        {
            var podetail = _applicationDbContext.PurchaseOrderDetails
                .Where(p => p.PurchaseOrderDetailId == Id).FirstOrDefault();
            var qtyDiff = _applicationDbContext.QtyDifferences.Where(q => q.PurchaseOrderId == podetail.PurchaseOrderId).FirstOrDefault();
            var qtyDiffDetail = _applicationDbContext.QtyDifferenceDetails
                .Where(d => d.QtyDifferenceId == qtyDiff.QtyDifferenceId && d.ProductNumber == podetail.ProductNumber).FirstOrDefault();
            return new JsonResult(qtyDiffDetail);
        }

        public IActionResult RedirectToIndex(string filterOptions = "", string searchTerm = "", DateTimeOffset? startDate = null, DateTimeOffset? endDate = null, int page = 1, int pageSize = 10)
        {
            try
            {
                // Format tanggal tanpa waktu
                string startDateString = startDate.HasValue ? startDate.Value.ToString("yyyy-MM-dd") : "";
                string endDateString = endDate.HasValue ? endDate.Value.ToString("yyyy-MM-dd") : "";

                // Bangun originalPath dengan format tanggal ISO 8601
                string originalPath = $"Page:Order/PurchaseOrder/Index?filterOptions={filterOptions}&searchTerm={searchTerm}&startDate={startDateString}&endDate={endDateString}&page={page}&pageSize={pageSize}";
                string encryptedPath = _protector.Protect(originalPath);

                // Hash GUID-like code (SHA256 truncated to 36 characters)
                string guidLikeCode = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(encryptedPath)))
                    .Replace('+', '-')
                    .Replace('/', '_')
                    .Substring(0, 36);

                // Simpan mapping GUID-like code ke encryptedPath di penyimpanan sementara (misalnya, cache)
                _urlMappingService.InMemoryMapping[guidLikeCode] = encryptedPath;

                return Redirect("/" + guidLikeCode);
            }
            catch
            {
                // Jika enkripsi gagal, kembalikan view
                return Redirect(Request.Path);
            }            
        }

        [HttpGet]
        public async Task<IActionResult> Index(string filterOptions = "", string searchTerm = "", DateTimeOffset? startDate = null, DateTimeOffset? endDate = null, int page = 1, int pageSize = 10)
        {
            ViewBag.Active = "PurchaseOrder";
            ViewBag.SearchTerm = searchTerm;
            ViewBag.SelectedFilter = filterOptions;

            // Format tanggal untuk input[type="date"]
            ViewBag.StartDate = startDate?.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate?.ToString("yyyy-MM-dd");

            // Format tanggal untuk tampilan (Indonesia)
            ViewBag.StartDateReadable = startDate?.ToString("dd MMMM yyyy");
            ViewBag.EndDateReadable = endDate?.ToString("dd MMMM yyyy");

            // Normalisasi tanggal untuk mengabaikan waktu
            if (startDate.HasValue) startDate = startDate.Value.Date;
            if (endDate.HasValue) endDate = endDate.Value.Date.AddDays(1).AddTicks(-1); // Sampai akhir hari

            // Tentukan range tanggal berdasarkan filterOptions
            if (!string.IsNullOrEmpty(filterOptions))
            {
                (startDate, endDate) = GetDateRangeHelper.GetDateRange(filterOptions);
            }

            var data = await _purchaseOrderRepository.GetAllPurchaseOrderPageSize(searchTerm, page, pageSize, startDate, endDate);

            var model = new Pagination<PurchaseOrder>
            {
                Items = data.purchaseOrders,
                TotalCount = data.totalCountPurchaseOrders,
                PageSize = pageSize,
                CurrentPage = page,
            };

            return View(model);
        }

        public IActionResult RedirectToDetail(Guid Id)
        {
            try
            {
                // Enkripsi path URL untuk "Index"
                string originalPath = $"Detail:Order/PurchaseOrder/DetailPurchaseOrder/{Id}";
                string encryptedPath = _protector.Protect(originalPath);

                // Hash GUID-like code (SHA256 truncated to 36 characters)
                string guidLikeCode = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(encryptedPath)))
                    .Replace('+', '-')
                    .Replace('/', '_')
                    .Substring(0, 36);

                // Simpan mapping GUID-like code ke encryptedPath di penyimpanan sementara (misalnya, cache)
                _urlMappingService.InMemoryMapping[guidLikeCode] = encryptedPath;

                return Redirect("/" + guidLikeCode);
            }
            catch
            {
                // Jika enkripsi gagal, kembalikan view
                return Redirect(Request.Path);
            }            
        }

        [HttpGet]
        public async Task<IActionResult> DetailPurchaseOrder(Guid Id)
        {
            ViewBag.Active = "PurchaseOrder";

            ViewBag.User = new SelectList(_userManager.Users, nameof(ApplicationUser.Id), nameof(ApplicationUser.NamaUser), SortOrder.Ascending);
            ViewBag.Pr = new SelectList(await _purchaseRequestRepository.GetPurchaseRequests(), "PurchaseRequestId", "PurchaseRequestNumber", SortOrder.Ascending);
            ViewBag.Product = new SelectList(await _productRepository.GetProducts(), "ProductId", "ProductName", SortOrder.Ascending);
            ViewBag.Approval = new SelectList(await _userActiveRepository.GetUserActives(), "UserActiveId", "FullName", SortOrder.Ascending);
            ViewBag.TermOfPayment = new SelectList(await _termOfPaymentRepository.GetTermOfPayments(), "TermOfPaymentId", "TermOfPaymentName", SortOrder.Ascending);

            var purchaseOrder = await _purchaseOrderRepository.GetPurchaseOrderById(Id);

            if (purchaseOrder == null)
            {
                Response.StatusCode = 404;
                return View("PurchaseOrderNotFound", Id);
            }

            PurchaseOrder model = new PurchaseOrder
            {
                PurchaseOrderId = purchaseOrder.PurchaseOrderId,
                PurchaseOrderNumber = purchaseOrder.PurchaseOrderNumber,
                PurchaseRequestId = purchaseOrder.PurchaseRequestId,
                PurchaseRequestNumber = purchaseOrder.PurchaseRequestNumber,
                UserAccessId = purchaseOrder.UserAccessId,
                UserApprove1Id = purchaseOrder.UserApprove1Id,
                UserApprove2Id = purchaseOrder.UserApprove2Id,
                UserApprove3Id = purchaseOrder.UserApprove3Id,
                TermOfPaymentId = purchaseOrder.TermOfPaymentId,
                //DueDate = purchaseOrder.DueDate,
                Status = purchaseOrder.Status,
                QtyTotal = purchaseOrder.QtyTotal,
                GrandTotal = Math.Truncate(purchaseOrder.GrandTotal),
                Note = purchaseOrder.Note
            };

            var ItemsList = new List<PurchaseOrderDetail>();

            var culture = new CultureInfo("id-ID");

            foreach (var item in purchaseOrder.PurchaseOrderDetails)
            {
                ItemsList.Add(new PurchaseOrderDetail
                {
                    ProductNumber = item.ProductNumber,
                    ProductName = item.ProductName,
                    Supplier = item.Supplier,
                    Measurement = item.Measurement,
                    Qty = item.Qty,
                    Price = Math.Truncate(item.Price),
                    Discount = item.Discount,
                    SubTotal = Math.Truncate(item.SubTotal)
                });
            }

            model.PurchaseOrderDetails = ItemsList;

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> GenerateNewPo(Guid Id)
        {
            ViewBag.Active = "PurchaseOrder";

            ViewBag.User = new SelectList(_userManager.Users, nameof(ApplicationUser.Id), nameof(ApplicationUser.NamaUser), SortOrder.Ascending);
            ViewBag.Product = new SelectList(await _productRepository.GetProducts(), "ProductId", "ProductName", SortOrder.Ascending);
            ViewBag.Approval = new SelectList(await _userActiveRepository.GetUserActives(), "UserActiveId", "FullName", SortOrder.Ascending);
            ViewBag.TermOfPayment = new SelectList(await _termOfPaymentRepository.GetTermOfPayments(), "TermOfPaymentId", "TermOfPaymentName", SortOrder.Ascending);

            PurchaseOrder purchaseOrder = _applicationDbContext.PurchaseOrders
                .Include(d => d.PurchaseOrderDetails)
                .Include(u => u.ApplicationUser)
                .Include(a1 => a1.UserApprove1)
                .Include(a2 => a2.UserApprove2)
                .Include(a3 => a3.UserApprove3)
                .Include(p => p.TermOfPayment)
                .Where(p => p.PurchaseOrderId == Id).FirstOrDefault();

            _signInManager.IsSignedIn(User);

            var getUser = _userActiveRepository.GetAllUserLogin().Where(u => u.UserName == User.Identity.Name).FirstOrDefault();

            PurchaseOrder po = new PurchaseOrder();

            var dateNow = DateTimeOffset.Now;
            var setDateNow = DateTimeOffset.Now.ToString("yyMMdd");

            var lastCode = _purchaseOrderRepository.GetAllPurchaseOrder().Where(d => d.CreateDateTime.ToString("yyMMdd") == dateNow.ToString("yyMMdd")).OrderByDescending(k => k.PurchaseOrderNumber).FirstOrDefault();
            if (lastCode == null)
            {
                po.PurchaseOrderNumber = "PO" + setDateNow + "0001";
            }
            else
            {
                var lastCodeTrim = lastCode.PurchaseOrderNumber.Substring(2, 6);

                if (lastCodeTrim != setDateNow)
                {
                    po.PurchaseOrderNumber = "PO" + setDateNow + "0001";
                }
                else
                {
                    po.PurchaseOrderNumber = "PO" + setDateNow + (Convert.ToInt32(lastCode.PurchaseOrderNumber.Substring(9, lastCode.PurchaseOrderNumber.Length - 9)) + 1).ToString("D4");
                }
            }

            ViewBag.PurchaseOrderNumber = po.PurchaseOrderNumber;

            var getQtyDiff = _qtyDifferenceRepository.GetAllQtyDifference().Where(po => po.PurchaseOrderId == purchaseOrder.PurchaseOrderId).FirstOrDefault();

            var getPo = new PurchaseOrder()
            {
                PurchaseRequestId = purchaseOrder.PurchaseRequestId,
                PurchaseRequestNumber = purchaseOrder.PurchaseRequestNumber,
                PurchaseOrderId = purchaseOrder.PurchaseOrderId,
                ExpiredDate = purchaseOrder.ExpiredDate,
                UserAccessId = purchaseOrder.UserAccessId,
                UserApprove1Id = purchaseOrder.UserApprove1Id,
                UserApprove2Id = purchaseOrder.UserApprove2Id,
                UserApprove3Id = purchaseOrder.UserApprove3Id,
                ApproveStatusUser1 = purchaseOrder.ApproveStatusUser1,
                ApproveStatusUser2 = purchaseOrder.ApproveStatusUser2,
                ApproveStatusUser3 = purchaseOrder.ApproveStatusUser3,
                TermOfPaymentId = purchaseOrder.TermOfPaymentId,
                Status = purchaseOrder.Status,
                QtyTotal = purchaseOrder.QtyTotal,
                GrandTotal = Math.Truncate(purchaseOrder.GrandTotal),
                Note = "Reference from PO Number " + purchaseOrder.PurchaseOrderNumber,
            };

            var ItemsList = new List<PurchaseOrderDetail>();

            foreach (var item in purchaseOrder.PurchaseOrderDetails)
            {
                if (purchaseOrder.PurchaseOrderDetails.Count != 1)
                {
                    foreach (var itemQtyDiff in getQtyDiff.QtyDifferenceDetails)
                    {
                        if (itemQtyDiff.ProductName == item.ProductName && itemQtyDiff.QtyReceive == item.Qty)
                        {
                            ItemsList.Add(new PurchaseOrderDetail
                            {
                                CreateDateTime = DateTime.Now,
                                CreateBy = new Guid(getUser.Id),
                                ProductNumber = item.ProductNumber,
                                ProductName = item.ProductName,
                                Supplier = item.Supplier,
                                Measurement = item.Measurement,
                                Qty = item.Qty,
                                Price = Math.Truncate(item.Price),
                                Discount = item.Discount,
                                SubTotal = Math.Truncate(item.SubTotal)
                            });
                        }
                    }
                }
                else {
                    ItemsList.Add(new PurchaseOrderDetail
                    {
                        CreateDateTime = DateTime.Now,
                        CreateBy = new Guid(getUser.Id),
                        ProductNumber = item.ProductNumber,
                        ProductName = item.ProductName,
                        Supplier = item.Supplier,
                        Measurement = item.Measurement,
                        Qty = item.Qty,
                        Price = Math.Truncate(item.Price),
                        Discount = item.Discount,
                        SubTotal = Math.Truncate(item.SubTotal)
                    });
                }
            }

            getPo.PurchaseOrderDetails = ItemsList;
            return View(getPo);
        }

        [HttpPost]
        public async Task<IActionResult> GenerateNewPo(PurchaseOrder model)
        {
            ViewBag.Active = "PurchaseOrder";

            if (ModelState.IsValid)
            {
                //PurchaseOrder purchaseOrder = await _purchaseOrderRepository.GetPurchaseOrderByIdNoTracking(model.PurchaseOrderId);

                _signInManager.IsSignedIn(User);

                var getUser = _userActiveRepository.GetAllUserLogin().Where(u => u.UserName == User.Identity.Name).FirstOrDefault();

                //string getPurchaseOrderNumber = Request.Form["PONumber"];

                var updatePurchaseRequest = _purchaseRequestRepository.GetAllPurchaseRequest().Where(c => c.PurchaseRequestId == model.PurchaseRequestId).FirstOrDefault();
                if (updatePurchaseRequest != null)
                {
                    updatePurchaseRequest.Status = model.PurchaseOrderNumber;
                    _applicationDbContext.Entry(updatePurchaseRequest).State = EntityState.Modified;
                }

                var updateStatusPo = _purchaseOrderRepository.GetAllPurchaseOrder().Where(p => p.PurchaseRequestId == model.PurchaseRequestId).FirstOrDefault();
                if (updateStatusPo != null)
                {
                    updateStatusPo.Status = "Cancelled";
                    _applicationDbContext.Entry(updateStatusPo).State = EntityState.Modified;
                }

                var newPurchaseOrder = new PurchaseOrder
                {
                    CreateDateTime = DateTime.Now,
                    CreateBy = new Guid(getUser.Id),
                    PurchaseRequestId = model.PurchaseRequestId,
                    PurchaseRequestNumber = model.PurchaseRequestNumber,
                    ExpiredDate = model.ExpiredDate,
                    UserAccessId = getUser.Id.ToString(),
                    UserApprove1Id = model.UserApprove1Id,
                    UserApprove2Id = model.UserApprove2Id,
                    UserApprove3Id = model.UserApprove3Id,
                    ApproveStatusUser1 = model.ApproveStatusUser1,
                    ApproveStatusUser2 = model.ApproveStatusUser2,
                    ApproveStatusUser3 = model.ApproveStatusUser3,
                    TermOfPaymentId = model.TermOfPaymentId,
                    Status = "In Order",
                    QtyTotal = model.QtyTotal,
                    GrandTotal = Math.Truncate(model.GrandTotal),
                    Note = model.Note
                };

                newPurchaseOrder.PurchaseOrderNumber = model.PurchaseOrderNumber;

                var ItemsList = new List<PurchaseOrderDetail>();

                foreach (var item in model.PurchaseOrderDetails)
                {
                    ItemsList.Add(new PurchaseOrderDetail
                    {
                        CreateDateTime = DateTime.Now,
                        CreateBy = new Guid(getUser.Id),
                        ProductNumber = item.ProductNumber,
                        ProductName = item.ProductName,
                        Supplier = item.Supplier,
                        Measurement = item.Measurement,
                        Qty = item.Qty,
                        Price = Math.Truncate(item.Price),
                        Discount = item.Discount,
                        SubTotal = Math.Truncate(item.SubTotal)
                    });
                }

                newPurchaseOrder.PurchaseOrderDetails = ItemsList;

                _purchaseOrderRepository.Tambah(newPurchaseOrder);

                TempData["SuccessMessage"] = "Number " + newPurchaseOrder.PurchaseOrderNumber + " Saved";
                return Json(new { redirectToUrl = Url.Action("Index", "PurchaseOrder") });
            }

            return View();
        }

        public async Task<IActionResult> PrintPurchaseOrder(Guid Id)
        {
            var purchaseOrder = await _purchaseOrderRepository.GetPurchaseOrderById(Id);

            var CreateDate = purchaseOrder.CreateDateTime.ToString("dd MMMM yyyy");
            var PoNumber = purchaseOrder.PurchaseOrderNumber;
            var CreateBy = purchaseOrder.ApplicationUser.NamaUser;
            var UserApprove1 = purchaseOrder.UserApprove1.FullName;
            var UserApprove2 = purchaseOrder.UserApprove2.FullName;
            var UserApprove3 = purchaseOrder.UserApprove3.FullName;
            var TermOfPayment = purchaseOrder.TermOfPayment.TermOfPaymentName;
            //var DueDate = purchaseOrder.DueDate;
            var Note = purchaseOrder.Note;
            var GrandTotal = purchaseOrder.GrandTotal;
            var Tax = (GrandTotal / 100) * 11;
            var GrandTotalAfterTax = (GrandTotal + Tax);

            WebReport web = new WebReport();
            var path = $"{_webHostEnvironment.WebRootPath}\\Reporting\\PurchaseOrder.frx";
            web.Report.Load(path);

            var msSqlDataConnection = new MsSqlDataConnection();
            msSqlDataConnection.ConnectionString = _configuration.GetConnectionString("DefaultConnection");
            var Conn = msSqlDataConnection.ConnectionString;

            web.Report.SetParameterValue("Conn", Conn);
            web.Report.SetParameterValue("PurchaseOrderId", Id.ToString());
            web.Report.SetParameterValue("PoNumber", PoNumber);
            web.Report.SetParameterValue("CreateDate", CreateDate);
            web.Report.SetParameterValue("CreateBy", CreateBy);
            web.Report.SetParameterValue("UserApprove1", UserApprove1);
            web.Report.SetParameterValue("UserApprove2", UserApprove2);
            web.Report.SetParameterValue("UserApprove3", UserApprove3);
            web.Report.SetParameterValue("TermOfPayment", TermOfPayment);
            web.Report.SetParameterValue("Note", Note);
            web.Report.SetParameterValue("GrandTotal", GrandTotal);
            web.Report.SetParameterValue("Tax", Tax);
            web.Report.SetParameterValue("GrandTotalAfterTax", GrandTotalAfterTax);

            web.Report.Prepare();
            Stream stream = new MemoryStream();
            web.Report.Export(new PDFSimpleExport(), stream);
            stream.Position = 0;
            return File(stream, "application/zip", (PoNumber + ".pdf"));
        }
    }
}