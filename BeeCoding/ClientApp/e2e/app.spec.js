import { test, expect } from '@playwright/test';

async function signIn(page, email = 'teacher@demo.test', password = 'password') {
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByLabel('Password').press('Enter');
  if (password === 'password') await page.waitForURL(/\/boards$/);   // the session cookie must exist before the test navigates on
}

test('a wrong password is refused and the right one lands on Boards', async ({ page }) => {
  await signIn(page, 'teacher@demo.test', 'not-the-password');
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page).toHaveURL(/\/login/);
  await signIn(page);
  await expect(page).toHaveURL(/\/boards$/);
  await expect(page).toHaveTitle('Boards · BeeCoding');
  await expect(page.getByRole('link', { name: /Demo Board/ })).toBeVisible();   // after "Loading…", not the empty-state card
});

test('the join-code QR dialog traps Tab, closes on Escape and gives focus back', async ({ page }) => {
  await signIn(page);
  await page.goto('/boards/demo-board');
  const open = page.getByRole('button', { name: 'Show QR' });
  await open.focus(); await open.click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  const inside = () => page.evaluate(() => !!document.activeElement?.closest('[role=dialog]'));
  expect(await inside()).toBe(true);
  for (let i = 0; i < 6; i++) await page.keyboard.press('Tab');
  expect(await inside()).toBe(true);
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(open).toBeFocused();
});

test('moving to another page changes the tab title and puts focus on the content', async ({ page }) => {
  await signIn(page);
  await expect(page).toHaveURL(/\/boards$/);
  await page.getByRole('link', { name: 'Practice' }).first().click();
  await expect(page).toHaveTitle('Practice · BeeCoding');
  await expect(page.locator('main#main')).toBeFocused();
});

test('a new student joins the demo board with its code', async ({ page }) => {
  const email = `kid${Date.now()}@test.dev`;
  await page.goto('/register');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel(/^Password/).first().fill('Passw0rd!x1');
  await page.getByLabel(/name/i).first().fill('Budi');
  await page.getByRole('button', { name: /create|register|sign up/i }).click();
  await expect(page).toHaveURL(/\/boards/);
  await page.goto('/join/DEMO01');
  await expect(page).toHaveURL(/\/boards\/demo-board/);
});
