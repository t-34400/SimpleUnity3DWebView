"""Generate stable metadata for new package assets without replacing existing GUIDs."""
from pathlib import Path
import uuid

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "Packages/com.t34400.vrwebview"
NAMESPACE = uuid.UUID("2051a168-107b-49da-a02b-6aba3a35942f")


def main():
    for path in [PACKAGE, *sorted(PACKAGE.rglob("*"))]:
        if path.suffix == ".meta":
            continue
        meta = path.with_name(path.name + ".meta")
        if meta.exists():
            continue
        guid = uuid.uuid5(NAMESPACE, path.relative_to(ROOT).as_posix()).hex
        body = f"fileFormatVersion: 2\nguid: {guid}\n"
        if path.is_dir():
            body += "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
        elif path.suffix == ".cs":
            body += "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
        elif path.suffix == ".aar":
            body += "PluginImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  iconMap: {}\n  executionOrder: {}\n  isPreloaded: 0\n  isOverridable: 0\n  isExplicitlyReferenced: 0\n  validateReferences: 1\n  platformData:\n  - first:\n      Any: \n    second:\n      enabled: 0\n      settings: {}\n  - first:\n      Android: Android\n    second:\n      enabled: 1\n      settings: {}\n  - first:\n      Editor: Editor\n    second:\n      enabled: 0\n      settings:\n        DefaultValueInitialized: true\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
        elif path.suffix == ".asmdef":
            body += "AssemblyDefinitionImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
        else:
            body += "DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
        meta.write_text(body, encoding="utf-8")


if __name__ == "__main__":
    main()
