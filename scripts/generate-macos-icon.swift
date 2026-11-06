// Draw the app icon as a native bitmap on macOS CI; no external image assets.
import AppKit
import Foundation

guard CommandLine.arguments.count == 2 else {
    fatalError("Usage: swift generate-macos-icon.swift OUTPUT.png")
}
let side = 1024
guard let bitmap = NSBitmapImageRep(
    bitmapDataPlanes: nil,
    pixelsWide: side, pixelsHigh: side,
    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
    colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0
), let context = NSGraphicsContext(bitmapImageRep: bitmap) else {
    fatalError("Offscreen bitmap context could not be created")
}
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = context

NSColor.clear.setFill()
NSRect(x: 0, y: 0, width: side, height: side).fill()
NSColor(calibratedRed: 0.055, green: 0.055, blue: 0.055, alpha: 1).setFill()
NSBezierPath(
    roundedRect: NSRect(x: 48, y: 48, width: 928, height: 928),
    xRadius: 203, yRadius: 203
).fill()

let yellow = NSColor(calibratedRed: 1.0, green: 0.824, blue: 0.16, alpha: 1.0)
yellow.setStroke()
let border = NSBezierPath(
    roundedRect: NSRect(x: 100, y: 100, width: 824, height: 824),
    xRadius: 155, yRadius: 155
)
border.lineWidth = 16
border.stroke()

// Stylized folder with a bold down-arrow.
let folder = NSBezierPath()
folder.move(to: NSPoint(x: 240, y: 366))
folder.line(to: NSPoint(x: 240, y: 666))
folder.curve(to: NSPoint(x: 295, y: 721),
             controlPoint1: NSPoint(x: 240, y: 697),
             controlPoint2: NSPoint(x: 261, y: 721))
folder.line(to: NSPoint(x: 422, y: 721))
folder.line(to: NSPoint(x: 490, y: 652))
folder.line(to: NSPoint(x: 720, y: 652))
folder.curve(to: NSPoint(x: 777, y: 597),
             controlPoint1: NSPoint(x: 757, y: 652),
             controlPoint2: NSPoint(x: 777, y: 631))
folder.line(to: NSPoint(x: 777, y: 366))
folder.curve(to: NSPoint(x: 720, y: 313),
             controlPoint1: NSPoint(x: 777, y: 336),
             controlPoint2: NSPoint(x: 751, y: 313))
folder.line(to: NSPoint(x: 295, y: 313))
folder.curve(to: NSPoint(x: 240, y: 366),
             controlPoint1: NSPoint(x: 260, y: 313),
             controlPoint2: NSPoint(x: 240, y: 338))
folder.close()
folder.lineWidth = 38
folder.lineCapStyle = .round
folder.lineJoinStyle = .round
folder.stroke()

let arrow = NSBezierPath()
arrow.move(to: NSPoint(x: 511, y: 608))
arrow.line(to: NSPoint(x: 511, y: 436))
arrow.move(to: NSPoint(x: 441, y: 506))
arrow.line(to: NSPoint(x: 511, y: 436))
arrow.line(to: NSPoint(x: 581, y: 506))
arrow.lineWidth = 36
arrow.lineCapStyle = .round
arrow.lineJoinStyle = .round
arrow.stroke()

context.flushGraphics()
NSGraphicsContext.restoreGraphicsState()
guard let data = bitmap.representation(using: .png, properties: [:]) else {
    fatalError("Could not serialize icon PNG")
}
try data.write(to: URL(fileURLWithPath: CommandLine.arguments[1]), options: .atomic)
