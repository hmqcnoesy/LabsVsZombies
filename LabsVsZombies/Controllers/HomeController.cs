﻿using System;
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
            ViewBag.AdGroups = allowedGroups.Split(',').ToList();
            ViewBag.IsAdmin = true;// ViewBag.AdGroups.Contains(User.Identity.Name);
            ViewBag.To = ConfigurationManager.AppSettings["notificationEmails"];
            return View(serviceNames);
        }


        public ActionResult IndexOLD()
        {
            var bgps = BGP.GetLocalhostBGPs();
            var allowedGroups = ConfigurationManager.AppSettings["allowedGroups"];
            var adGroups = allowedGroups.Split(',').ToList();
            ViewBag.IsAdmin = adGroups.Any(g => User.IsInRole(g));
            ViewBag.AdGroups = adGroups;
            ViewBag.To = ConfigurationManager.AppSettings["notificationEmails"];
            return View(bgps);
        }


        public ActionResult Info(string serviceName)
        {
            var bgp = BGP.GetLocalhostBGP(serviceName);
            var allowedGroups = ConfigurationManager.AppSettings["allowedGroups"];
            var adGroups = allowedGroups.Split(',').ToList();
            ViewBag.IsAdmin = adGroups.Any(g => User.IsInRole(g));
            ViewBag.To = ConfigurationManager.AppSettings["notificationEmails"];
            return View(bgp);
        }


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
                return Json(BGP.GetLocalhostBGP(serviceName));
            }
            catch (Exception ex)
            {
                return new HttpStatusCodeResult(500, ex.Message);
            }
        }
    }
}