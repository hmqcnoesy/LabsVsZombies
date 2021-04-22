using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceProcess;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using LabsVsZombies.Models;
using System.Management;
using System.Configuration;

namespace LabsVsZombies.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            var serviceNames = BGP.GetLocalhostBgpNames();
            var allowedGroups = ConfigurationManager.AppSettings["allowedGroups"];
            var adGroups = allowedGroups.Split(',').ToList();
            ViewBag.AdGroups = adGroups;
            ViewBag.IsAdmin = adGroups.Any(g => User.IsInRole(g));
            ViewBag.To = ConfigurationManager.AppSettings["notificationEmails"];
            ViewBag.Sql = BGP.GetDbQuery();
            return View(serviceNames);
        }


        [OutputCache(NoStore = true, Duration = 0)]
        public ActionResult GetService(string serviceName)
        {
            var bgp = BGP.GetLocalhostBGP(serviceName);
            return Json(bgp, JsonRequestBehavior.AllowGet);
        }


        [HttpPost]
        public ActionResult RestartBgp(string serviceName)
        {
            var allowedGroups = ConfigurationManager.AppSettings["allowedGroups"];
            var adGroups = allowedGroups.Split(',').ToList();
            if (!adGroups.Any(g => User.IsInRole(g)))
                return new HttpUnauthorizedResult();

            try
            {
                var bgp = BGP.GetLocalhostBGP(serviceName);
                if (bgp == null) return HttpNotFound();
                bgp.RestartBgp();
                System.Threading.Thread.Sleep(1000); // just give it a bit before checking on nautilus.exe db connection status, etc.
                return Json(BGP.GetLocalhostBGP(serviceName));
            }
            catch (Exception ex)
            {
                return new HttpStatusCodeResult(500, ex.Message);
            }
        }
    }
}