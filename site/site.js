// Detangle's website, in full. No framework, no dependencies, no network calls.
// Everything below is progressive: with JavaScript disabled the page still reads, the
// download panels still stack, and every command is still selectable text.
(function () {
  "use strict";

  // --- theme toggle --------------------------------------------------------
  // The pre-paint script in the head applies a stored choice. This adds the switch and
  // keeps following the OS while no explicit choice has been made.
  var root = document.documentElement;
  var toggle = document.getElementById("theme-toggle");
  var media = window.matchMedia("(prefers-color-scheme: dark)");

  function store(value) {
    try {
      if (value) {
        localStorage.setItem("detangle-theme", value);
      } else {
        localStorage.removeItem("detangle-theme");
      }
    } catch (e) {
      // Private browsing. The choice still applies for this page view.
    }
  }

  function current() {
    return root.getAttribute("data-theme") || (media.matches ? "dark" : "light");
  }

  if (toggle) {
    toggle.addEventListener("click", function () {
      var next = current() === "dark" ? "light" : "dark";
      root.setAttribute("data-theme", next);
      store(next);
    });
  }

  media.addEventListener("change", function () {
    if (!root.getAttribute("data-theme")) {
      // Nothing stored, so the operating system is still in charge; the stylesheet
      // handles the repaint and there is nothing to do but let it.
    }
  });

  // --- copy buttons --------------------------------------------------------

  function flash(button) {
    var original = button.textContent;
    button.textContent = "Copied";
    setTimeout(function () {
      button.textContent = original;
    }, 1500);
  }

  function copy(text, button) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text).then(function () {
        flash(button);
      });
      return;
    }

    var area = document.createElement("textarea");
    area.value = text;
    document.body.appendChild(area);
    area.select();
    try {
      document.execCommand("copy");
      flash(button);
    } catch (e) {
      // Nothing to do; the text is on screen and selectable either way.
    }
    document.body.removeChild(area);
  }

  document.querySelectorAll("[data-copy]").forEach(function (button) {
    button.addEventListener("click", function () {
      var target = document.getElementById(button.getAttribute("data-copy"));
      if (target) {
        copy(target.textContent, button);
      }
    });
  });

  document.querySelectorAll("[data-copy-command]").forEach(function (button) {
    button.addEventListener("click", function () {
      var command = button.parentNode.querySelector("[data-command]");
      if (command) {
        copy(command.textContent, button);
      }
    });
  });

  // --- OS panels become tabs, but only once this runs ----------------------

  var panels = document.getElementById("os-panels");

  if (panels) {
    panels.classList.add("js-tabs");

    var tabs = panels.querySelectorAll(".tab");
    var sections = panels.querySelectorAll(".os");

    // Preselect the platform the visitor is on. Getting this wrong costs one click;
    // hiding the other two behind a menu would cost more.
    var platform = navigator.platform || "";
    var guess = /Mac/i.test(platform) ? "macos" : /Win/i.test(platform) ? "windows" : /Linux/i.test(platform) ? "linux" : null;

    function select(os) {
      tabs.forEach(function (tab) {
        tab.setAttribute("aria-selected", String(tab.getAttribute("data-os") === os));
      });

      sections.forEach(function (section) {
        section.classList.toggle("active", section.getAttribute("data-os") === os);
      });
    }

    tabs.forEach(function (tab) {
      tab.addEventListener("click", function () {
        select(tab.getAttribute("data-os"));
      });
    });

    if (guess) {
      select(guess);
    }
  }
})();
