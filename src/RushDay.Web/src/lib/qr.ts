/**
 * The TOTP setup QR code (05-frontend.md section 10, `/account/mfa`). `qrcode` is imported
 * dynamically so it lands in its own chunk that only the MFA setup page downloads
 * (scripts/bundle-budget.mjs fails the build if it leaks into the entry graph). The result is a
 * `data:` PNG, which CSP `img-src 'self' data:` allows (03-security.md section 4); no remote image
 * service ever sees the secret.
 */
export async function qrDataUrl(text: string): Promise<string> {
  const { toDataURL } = await import('qrcode')
  return toDataURL(text, {
    errorCorrectionLevel: 'M',
    margin: 1,
    width: 240,
    color: { dark: '#1a1d21', light: '#ffffff' },
  })
}
