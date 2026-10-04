// SPDX-License-Identifier: MIT

/// A native display identity and the capabilities needed for safe selection.
public struct DisplayCandidate: Sendable, Equatable {
    public let id: String
    public let isPhysical: Bool
    public let isCloned: Bool
    public let isHDR: Bool

    /// Describes an enumerated target without carrying thread-affine native handles.
    public init(id: String, isPhysical: Bool, isCloned: Bool = false, isHDR: Bool = false) {
        self.id = id
        self.isPhysical = isPhysical
        self.isCloned = isCloned
        self.isHDR = isHDR
    }
}
/// Chooses only the saved target, or a unique physical SDR display on first start.
public func selectDisplay(from candidates: [DisplayCandidate], savedID: String?)
    throws(BrightnessError) -> DisplayCandidate
{
    let supported = candidates.filter { $0.isPhysical && !$0.isCloned && !$0.isHDR }
    if let savedID, !savedID.isEmpty {
        guard let target = supported.first(where: { $0.id == savedID }) else {
            throw .displayDisconnected(savedID)
        }
        return target
    }
    guard supported.count == 1 else { throw .ambiguousDisplay }
    return supported[0]
}
