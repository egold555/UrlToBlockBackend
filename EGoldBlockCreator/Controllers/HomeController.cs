﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace EGoldBlockCreator.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            return Redirect("https://www.spigotmc.org/resources/urltoblock.40330/");
        }

    }
}