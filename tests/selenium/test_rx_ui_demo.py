import os
import shutil
import time
from uuid import uuid4

import pytest


pytestmark = pytest.mark.selenium


def _require_demo_enabled():
    if os.getenv("RX_UI_SELENIUM") != "1":
        pytest.skip("Set RX_UI_SELENIUM=1 to run the visible Selenium UI demo.")


def _driver():
    try:
        from selenium import webdriver
        from selenium.webdriver.chrome.options import Options
        from selenium.webdriver.chrome.service import Service
    except ModuleNotFoundError:
        pytest.skip("Install Selenium dependencies with: pip install -r requirements-selenium.txt")

    options = Options()
    chrome_binary = (
        os.getenv("RX_UI_CHROME_BINARY")
        or shutil.which("google-chrome")
        or shutil.which("google-chrome-stable")
        or shutil.which("chromium")
        or shutil.which("chromium-browser")
    )
    if chrome_binary:
        options.binary_location = chrome_binary
    if os.getenv("RX_UI_HEADLESS") == "1":
        options.add_argument("--headless=new")
    options.add_argument("--window-size=1280,900")
    options.add_argument("--disable-dev-shm-usage")
    options.add_argument("--no-first-run")
    options.add_argument("--no-default-browser-check")

    profile_dir = os.getenv("RX_UI_CHROME_PROFILE_DIR")
    if profile_dir:
        options.add_argument(f"--user-data-dir={profile_dir}")

    chromedriver = os.getenv("RX_UI_CHROMEDRIVER") or shutil.which("chromedriver")
    print(
        f"Launching Chrome binary={chrome_binary or 'auto'} "
        f"chromedriver={chromedriver or 'auto'} headless={os.getenv('RX_UI_HEADLESS') == '1'}",
        flush=True,
    )
    service = Service(executable_path=chromedriver) if chromedriver else Service()
    return webdriver.Chrome(service=service, options=options)


def _click(driver, by, value):
    from selenium.webdriver.support import expected_conditions as ec
    from selenium.webdriver.support.ui import WebDriverWait

    wait = WebDriverWait(driver, 20)
    element = wait.until(ec.element_to_be_clickable((by, value)))
    element.click()
    return element


def _type(driver, by, value, text):
    from selenium.webdriver.support import expected_conditions as ec
    from selenium.webdriver.support.ui import WebDriverWait

    wait = WebDriverWait(driver, 20)
    element = wait.until(ec.visibility_of_element_located((by, value)))
    element.clear()
    element.send_keys(text)
    return element


def test_rx_ui_visible_workflow():
    _require_demo_enabled()

    from selenium.webdriver.common.by import By
    from selenium.webdriver.support import expected_conditions as ec
    from selenium.webdriver.support.ui import WebDriverWait

    base_url = os.getenv("RX_UI_BASE_URL", "http://192.168.1.239:30080").rstrip("/")
    pause_seconds = float(os.getenv("RX_UI_STEP_PAUSE_SECONDS", "1.25"))
    loop_count = int(os.getenv("RX_UI_LOOP_COUNT", "1"))
    loop_delay_seconds = float(os.getenv("RX_UI_LOOP_DELAY_SECONDS", "2"))
    fixed_rx_id = os.getenv("RX_UI_RX_ID")

    driver = _driver()
    wait = WebDriverWait(driver, 20)
    try:
        iteration = 0
        while loop_count <= 0 or iteration < loop_count:
            iteration += 1
            rx_id = fixed_rx_id or f"RX-SELENIUM-{uuid4().hex[:8].upper()}"
            print(f"UI demo iteration {iteration} rx_id={rx_id}", flush=True)

            driver.get(f"{base_url}/")
            wait.until(ec.text_to_be_present_in_element((By.TAG_NAME, "h1"), "Prescription Demo UI"))
            time.sleep(pause_seconds)

            _type(driver, By.ID, "rx", rx_id)
            _type(driver, By.ID, "refill", "1")

            _click(driver, By.XPATH, "//button[normalize-space()='Lookup']")
            wait.until(ec.text_to_be_present_in_element((By.TAG_NAME, "pre"), rx_id))
            time.sleep(pause_seconds)

            _type(driver, By.ID, "rx", rx_id)
            _click(driver, By.XPATH, "//button[normalize-space()='Approve']")
            wait.until(ec.text_to_be_present_in_element((By.TAG_NAME, "pre"), "ApproveQueued"))
            time.sleep(pause_seconds)

            _type(driver, By.ID, "rx", rx_id)
            _type(driver, By.ID, "refill", "1")
            _click(driver, By.XPATH, "//button[normalize-space()='Refill']")
            wait.until(ec.text_to_be_present_in_element((By.TAG_NAME, "pre"), "RefillQueued"))
            time.sleep(pause_seconds)

            driver.get(f"{base_url}/?PageSize=10&PageNumber=1")
            wait.until(ec.presence_of_element_located((By.TAG_NAME, "table")))
            time.sleep(pause_seconds)

            if loop_count <= 0 or iteration < loop_count:
                time.sleep(loop_delay_seconds)
    finally:
        if os.getenv("RX_UI_KEEP_BROWSER_OPEN") == "1":
            input("Press Enter to close the Selenium browser...")
        driver.quit()
