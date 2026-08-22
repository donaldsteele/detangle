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

  // --- which file to download ----------------------------------------------
  // A release carries forty-three files. Guessing which one a visitor wants is worth
  // doing and not worth trusting: this fills in a recommendation and highlights one card,
  // and never hides the other two. Getting it wrong has to cost a glance, not a download.

  var pick = document.getElementById("pick");

  function architecture() {
    // Only Chromium tells the truth here, and only asynchronously. Everything else gets
    // the honest answer, which is "probably the common one" — said out loud below rather
    // than hidden behind a confident-looking button.
    var data = navigator.userAgentData;

    if (data && data.getHighEntropyValues) {
      return data.getHighEntropyValues(["architecture"]).then(function (values) {
        return /arm/i.test(values.architecture || "") ? "arm64" : "x64";
      }).catch(function () {
        return null;
      });
    }

    return Promise.resolve(null);
  }

  function platform() {
    var data = navigator.userAgentData;
    var name = (data && data.platform) || navigator.platform || "";
    var agent = navigator.userAgent || "";

    // An iPad reports itself as a Mac and cannot run any of these, so it is worth telling
    // apart rather than handing a .pkg to somebody who has no way to open it.
    if (/iPhone|iPad|iPod/i.test(agent) || (/Mac/i.test(name) && navigator.maxTouchPoints > 2)) {
      return "ios";
    }

    if (/Android/i.test(agent)) {
      return "android";
    }

    if (/Mac|Darwin/i.test(name)) {
      return "macos";
    }

    if (/Win/i.test(name)) {
      return "windows";
    }

    if (/Linux|X11|CrOS/i.test(name)) {
      return "linux";
    }

    return null;
  }

  // What each platform's obvious first download is, and what to say about the choice of
  // architecture when the browser would not say.
  var RECOMMENDED = {
    macos: {
      label: "macOS",
      file: function (arch) {
        return arch === "x64"
          ? "Detangle-osx-x64-Setup.pkg"
          : "Detangle-osx-arm64-Setup.pkg";
      },
      unsure: "Assuming Apple Silicon, which is every Mac since 2020. On an Intel Mac take the x64 installer below."
    },
    windows: {
      label: "Windows",
      file: function (arch) {
        return arch === "arm64"
          ? "Detangle-win-arm64-Setup.exe"
          : "Detangle-win-x64-Setup.exe";
      },
      unsure: "Assuming Intel or AMD. On an ARM machine take the arm64 installer below."
    },
    linux: {
      label: "Linux",
      file: function (arch) {
        return arch === "arm64"
          ? "Detangle-linux-arm64.AppImage"
          : "Detangle-linux-x64.AppImage";
      },
      unsure: "Assuming x86-64. On ARM take the arm64 AppImage below."
    }
  };

  function element(tag, className, text) {
    var node = document.createElement(tag);

    if (className) {
      node.className = className;
    }

    if (text) {
      node.textContent = text;
    }

    return node;
  }

  function recommend(os, arch) {
    var entry = RECOMMENDED[os];

    if (!pick || !entry) {
      return;
    }

    var file = entry.file(arch);
    var card = document.querySelector('.dl[data-os="' + os + '"]');

    if (card) {
      card.classList.add("mine");
    }

    var lead = pick.querySelector(".pick-lead");
    var note = pick.querySelector(".pick-note");

    lead.textContent = "You look like you are on " + entry.label + ".";

    var row = element("div", "pick-row");
    var button = element("a", "button primary", "Download for " + entry.label);

    button.href =
      "https://github.com/donaldsteele/detangle/releases/latest/download/" + file;

    row.appendChild(button);
    row.appendChild(element("span", "pick-file", file));

    pick.insertBefore(row, note);
    pick.classList.add("found");

    note.textContent = arch
      ? "Everything else is below, including the portable build."
      : entry.unsure;
  }

  if (pick) {
    var os = platform();

    if (os === "ios" || os === "android") {
      // There is no build for these and there is not going to be one, so say so and point
      // at the thing that does work in a phone browser.
      pick.querySelector(".pick-lead").textContent =
        "Detangle is a desktop application.";
      pick.querySelector(".pick-note").innerHTML =
        'There is no mobile build. The <a href="#demo">browser demo</a> runs the same reader in this tab.';
    } else if (os) {
      architecture().then(function (arch) {
        recommend(os, arch);
      });
    }
  }

  // --- the demo loads when it is asked to ----------------------------------
  // The demo is about 11 MB of WebAssembly runtime. The frame used to carry
  // loading="lazy", which only postponed it until the frame neared the viewport, so
  // anybody who scrolled past this section paid for it whether they wanted it or not.
  // Now the poster stays until the control is used. That control is an <a href="demo/">,
  // so with JavaScript off it still opens the demo in its own tab; all this does is
  // upgrade it into an in-place load. The handler lives here rather than inline because
  // the site's CSP allows no inline script beyond the hashed theme pre-paint.

  var demoFrame = document.getElementById("demo-frame");
  var demoStart = document.getElementById("demo-start");

  if (demoFrame && demoStart) {
    demoStart.addEventListener("click", function (event) {
      // A modified click means "open it somewhere else"; leave the link alone.
      if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
        return;
      }

      event.preventDefault();

      var frame = document.createElement("iframe");
      frame.title = "Detangle running in the browser";
      frame.src = "demo/";

      demoFrame.classList.add("loaded");
      demoFrame.appendChild(frame);
    });
  }
})();
